using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class ReportesSospechososService(IApplicationDbContext db, IAuditoriaService auditoria, GamificacionService juego)
{
    // mensajes paginados, pendientes trae todos los aun reportables
    public async Task<object> ListarAsync(int usuario, CancellationToken ct, int? pagina = null, int? tamanio = null)
    {
        var now = DateTime.UtcNow;
        var query = db.EnviosSimulacion.AsNoTracking().Where(e => e.Id_Usuario == usuario && e.EstadoEntrega == "accepted"
            && ((e.Campania.Id_Estado == 2 && (e.Campania.FechaFinProgramada == null || e.Campania.FechaFinProgramada >= now))
                || e.FechaReporte != null)).OrderByDescending(e => e.FechaEnvio).ThenByDescending(e => e.Id_Envio);
        var vista = query.Select(e => new {
            token = e.TokenUuid,
            asunto = e.Campania.AsuntoRegistro ?? e.Campania.Plantilla.AsuntoCorreo,
            e.FechaEnvio,
            e.FechaReporte,
            reportado = e.FechaReporte != null,
            //estado del reporte de simulacion (distinto de incidentes reales)
            estadoReporte = e.FechaReporte == null ? null : e.ReporteSinClickPrevio ? "valido" : "tras_clic",
            puedeReportar = e.FechaReporte == null && e.Campania.Id_Estado == 2 &&
                    (e.Campania.FechaFinProgramada == null || e.Campania.FechaFinProgramada >= now)
        });
        var actual = await vista.PaginarAsync(pagina, tamanio, ct);
        var pendientes = await vista.Where(m => m.puedeReportar).ToListAsync(ct);
        return new { items = actual.Items, total = actual.Total, pagina = actual.Pagina, tamanio = actual.Tamanio, pendientes };
    }

    public async Task<object> ConsultarAsync(Guid token, int usuario, CancellationToken ct)
    {
        var e = await BuscarAsync(token, usuario, ct);
        var activo = Activo(e, DateTime.UtcNow);
        return new {
            token = e.TokenUuid,
            asunto = e.Campania.AsuntoRegistro ?? e.Campania.Plantilla.AsuntoCorreo,
            e.FechaEnvio,
            reportado = e.FechaReporte.HasValue,
            e.FechaReporte,
            e.ReporteSinClickPrevio,
            estadoReporte = e.FechaReporte == null ? null : e.ReporteSinClickPrevio ? "valido" : "tras_clic",
            puedeReportar = activo && !e.FechaReporte.HasValue,
            mensaje = e.FechaReporte.HasValue ? "Este mensaje ya tiene un reporte registrado." : activo
                ? "Confirma si deseas reportar este mensaje como sospechoso." : "La evaluación ya concluyó o el envío no está confirmado; no se admiten nuevos reportes."
        };
    }

    public async Task<object> ReportarAsync(Guid token, int usuario, bool confirmar, CancellationToken ct)
    {
        if (!confirmar)
            throw new BusinessRuleException("Confirma el reporte antes de enviarlo.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // la consulta inicial tambien impide distinguir tokens de otras personas
        var envio = await BuscarAsync(token, usuario, ct);
        // orden de bloqueos compartido con envio y cierre/ campania y despues envio
        await db.CampaniasSimulacion.Where(c => c.Id_Campania == envio.Id_Campania)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Id_Estado, c => c.Id_Estado), ct);
        await db.EnviosSimulacion.Where(e => e.TokenUuid == token && e.Id_Usuario == usuario)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EstadoEntrega, e => e.EstadoEntrega), ct);
        envio = await BuscarAsync(token, usuario, ct);

        if (envio.FechaReporte.HasValue)
            return new { yaReportado = true, envio.FechaReporte, envio.ReporteSinClickPrevio, mensaje = "Ya existe un reporte de este mensaje. No se ha duplicado su valoración." };
        var now = DateTime.UtcNow;

        if (!Activo(envio, now))
            throw new BusinessRuleException("La evaluación ya concluyó o el envío no está confirmado; no se puede registrar el reporte.", BusinessError.Conflict);
        var sinClick = !envio.HizoClick;
        await db.EnviosSimulacion.Where(e => e.TokenUuid == token && e.Id_Usuario == usuario && e.FechaReporte == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.FechaReporte, now).SetProperty(e => e.ReporteSinClickPrevio, sinClick), ct);
        var fechaReporte = await db.EnviosSimulacion.Where(e => e.TokenUuid == token && e.Id_Usuario == usuario).Select(e => e.FechaReporte).SingleAsync(ct);
        await auditoria.RegistrarAsync("ReportarMensajeSospechoso", true, usuario, entidad: "EnvioSimulacion",
            idEntidadAfectada: envio.Id_Envio.ToString(), detalles: new { idCampania = envio.Id_Campania, sinClickPrevio = sinClick });

        // gamificacion: reportar se reconoce una sola vez por envio (clave unica)
        await juego.BloquearUsuarioAsync(usuario, ct);
        var puntos = (await juego.ReglasAsync(ct)).PuntosReporte;
        var asunto = envio.Campania.AsuntoRegistro ?? envio.Campania.Plantilla.AsuntoCorreo;
        var logros = await juego.ReconocerAsync(usuario, $"reporte:{envio.Id_Envio}", $"Reporte de simulación «{asunto}»", puntos, ct);
        await tx.CommitAsync(ct);

        return new {
            yaReportado = false,
            FechaReporte = fechaReporte,
            ReporteSinClickPrevio = sinClick,
            puntosGanados = puntos,
            insigniasDesbloqueadas = logros,
            mensaje = puntos > 0 ? $"Reporte recibido. Ganaste {puntos} puntos y tu CyberScore mejora." : "Reporte recibido. Quedó registrado en tu CyberScore."
        };
    }

    private async Task<EnvioSimulacion> BuscarAsync(Guid token, int usuario, CancellationToken ct) =>
        await db.EnviosSimulacion.AsNoTracking().Include(e => e.Campania).ThenInclude(c => c.Plantilla)
            .SingleOrDefaultAsync(e => e.TokenUuid == token && e.Id_Usuario == usuario, ct)
            ?? throw new BusinessRuleException("No se encontró un mensaje disponible para tu cuenta.", BusinessError.NotFound);
    private static bool Activo(EnvioSimulacion e, DateTime now) => e.EstadoEntrega == "accepted" && e.Campania.Id_Estado == 2
        && !e.Campania.FechaFinalizacion.HasValue && (!e.Campania.FechaFinProgramada.HasValue || e.Campania.FechaFinProgramada >= now);
}
