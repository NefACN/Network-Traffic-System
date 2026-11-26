using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// gobernanza etica de campanias de simulacion
public sealed class GobernanzaService(IApplicationDbContext db, IAuditoriaService auditoria)
{
    public static readonly IReadOnlyList<string> MotivosExclusion = ["licencia", "vacaciones", "incidente_reciente", "solicitud", "otro"];
    private static readonly TimeSpan Bolivia = TimeSpan.FromHours(-4);

    public Task<PoliticaGobernanza> PoliticaAsync(CancellationToken ct = default) =>
        db.Set<PoliticaGobernanza>().AsNoTracking().SingleAsync(ct);

    public static List<string> Terminos(PoliticaGobernanza p) =>
        JsonSerializer.Deserialize<List<string>>(p.PretextosProhibidos) ?? [];

    // normaliza el texto para comparar terminos de forma robusta
    public static string Normalizar(string texto)
    {
        var sinHtml = Regex.Replace(texto ?? "", "<[^>]*>", " ");
        sinHtml = System.Net.WebUtility.HtmlDecode(sinHtml);
        var descompuesto = sinHtml.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"\s+", " ");
    }

    // terminos prohibidos detectados en el texto
    public static List<string> PretextosDetectados(string texto, IEnumerable<string> terminos)
    {
        var normal = Normalizar(texto);

        return terminos.Where(t => !string.IsNullOrWhiteSpace(t))
            .Where(t => Regex.IsMatch(normal, @"(?<![\p{L}\p{N}])" + Regex.Escape(Normalizar(t).Trim())))
            .Distinct().ToList();
    }

    // indica si la hora local esta fuera de la ventana de envio permitida
    public static string? FueraDeVentana(PoliticaGobernanza p, DateTime utc)
    {
        if (!p.VentanaActiva)
            return null;

        var local = utc + Bolivia;
        var habil = local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        if ((p.SoloDiasHabiles && !habil) || local.Hour < p.HoraInicio || local.Hour >= p.HoraFin)
            return $"Fuera de la ventana de envío: {p.HoraInicio:00}:00–{p.HoraFin:00}:00{(p.SoloDiasHabiles ? ", de lunes a viernes" : "")} (hora de Bolivia). Inicia la campaña dentro de ese horario.";

        return null;
    }

    // exclusiones vigentes en el momento actual
    public IQueryable<int> Excluidos(DateTime ahora) => db.Set<ExclusionSimulacion>()
        .Where(x => x.Activa && x.Desde <= ahora && (x.Hasta == null || x.Hasta > ahora)).Select(x => x.IdUsuario);

    // personas que alcanzaron el maximo de simulaciones en 30 dias
    public async Task<HashSet<int>> SaturadosAsync(IEnumerable<int> usuarios, int excluirCampania, PoliticaGobernanza p, CancellationToken ct)
    {
        if (p.MaxSimulacionesPorPersona <= 0)
            return [];

        var ids = usuarios.ToList();
        var desde = DateTime.UtcNow.AddDays(-30);

        return (await db.EnviosSimulacion.AsNoTracking()
            .Where(e => ids.Contains(e.Id_Usuario) && e.Id_Campania != excluirCampania && (e.EstadoEntrega == "accepted" || e.EstadoEntrega == "pending")
                && (e.FechaEnvio >= desde || (e.FechaEnvio == null && e.EstadoEntrega == "pending")))
            .GroupBy(e => e.Id_Usuario).Where(g => g.Count() >= p.MaxSimulacionesPorPersona).Select(g => g.Key).ToListAsync(ct)).ToHashSet();
    }

    public async Task GuardarPoliticaAsync(PoliticaGobernanzaRequest r, int actor, CancellationToken ct)
    {
        if (r.HoraInicio >= r.HoraFin)
            throw new BusinessRuleException("La hora de inicio debe ser anterior a la hora de fin.");

        var terminos = (r.PretextosProhibidos ?? []).Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (terminos.Count > 100 || terminos.Any(t => t.Length > 60))
            throw new BusinessRuleException("Registra hasta 100 pretextos de máximo 60 caracteres.");

        var p = await db.Set<PoliticaGobernanza>().SingleAsync(ct);

        p.RequiereAprobacion = r.RequiereAprobacion;
        p.VentanaActiva = r.VentanaActiva;
        p.HoraInicio = r.HoraInicio;
        p.HoraFin = r.HoraFin;
        p.SoloDiasHabiles = r.SoloDiasHabiles;
        p.MaxSimulacionesPorPersona = r.MaxSimulacionesPorPersona;
        p.UmbralAgregacion = r.UmbralAgregacion;
        p.PretextosProhibidos = JsonSerializer.Serialize(terminos);
        p.Revision++;
        p.ActualizadaEn = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync("PoliticaGobernanza", true, actor, entidad: "PoliticaGobernanza",
            detalles: new {
                r.RequiereAprobacion,
                r.VentanaActiva,
                r.HoraInicio,
                r.HoraFin,
                r.SoloDiasHabiles,
                r.MaxSimulacionesPorPersona,
                r.UmbralAgregacion,
                pretextos = terminos.Count,
                p.Revision
            });
    }

    // exclusiones paginadas, las mas recientes primero
    public async Task<object> ExclusionesAsync(bool soloVigentes, CancellationToken ct, int? pagina = null, int? tamanio = null)
    {
        var ahora = DateTime.UtcNow;
        var q = db.Set<ExclusionSimulacion>().AsNoTracking();

        if (soloVigentes)
            q = q.Where(x => x.Activa && x.Desde <= ahora && (x.Hasta == null || x.Hasta > ahora));

        return await q.OrderByDescending(x => x.FechaCreacion).ThenByDescending(x => x.Id).Select(x => new {
            x.Id,
            x.IdUsuario,
            nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido,
            correo = x.Usuario.Email,
            x.Motivo,
            x.Nota,
            x.Desde,
            x.Hasta,
            x.Activa,
            vigente = x.Activa && x.Desde <= ahora && (x.Hasta == null || x.Hasta > ahora),
            x.FechaCreacion,
        }).PaginarAsync(pagina, tamanio, ct);
    }

    public async Task<int> ExcluirAsync(ExclusionRequest r, int actor, CancellationToken ct)
    {
        if (!MotivosExclusion.Contains(r.Motivo))
            throw new BusinessRuleException("Selecciona un motivo de exclusión válido.");

        var desde = (r.Desde ?? DateTime.UtcNow).ToUniversalTime();
        var hasta = r.Hasta?.ToUniversalTime();

        if (hasta <= desde)
            throw new BusinessRuleException("La fecha de fin de la exclusión debe ser posterior al inicio.");

        if (!await db.Users.AnyAsync(u => u.Id == r.IdUsuario, ct))
            throw new BusinessRuleException("El colaborador no existe.", BusinessError.NotFound);

        var x = new ExclusionSimulacion {
            IdUsuario = r.IdUsuario,
            Motivo = r.Motivo,
            Nota = string.IsNullOrWhiteSpace(r.Nota) ? null : r.Nota.Trim(),
            Desde = DateTime.SpecifyKind(desde, DateTimeKind.Utc),
            Hasta = hasta is null ? null : DateTime.SpecifyKind(hasta.Value, DateTimeKind.Utc),
            CreadoPor = actor
        };

        db.Set<ExclusionSimulacion>().Add(x);

        await db.SaveChangesAsync(ct);
        // el motivo no se copia en la auditoria (datos personales)
        await auditoria.RegistrarAsync("ExcluirDeSimulaciones", true, actor, entidad: "ExclusionSimulacion", idEntidadAfectada: x.Id.ToString(),
            detalles: new { x.IdUsuario, x.Motivo, x.Desde, x.Hasta });
        return x.Id;
    }

    public async Task RevocarAsync(int id, int actor, CancellationToken ct)
    {
        var n = await db.Set<ExclusionSimulacion>().Where(x => x.Id == id && x.Activa).ExecuteUpdateAsync(s => s.SetProperty(x => x.Activa, false), ct);
        if (n == 0 && !await db.Set<ExclusionSimulacion>().AnyAsync(x => x.Id == id, ct))
            throw new BusinessRuleException("La exclusión no existe.", BusinessError.NotFound);

        await auditoria.RegistrarAsync("RevocarExclusion", true, actor, entidad: "ExclusionSimulacion", idEntidadAfectada: id.ToString());
    }
}
