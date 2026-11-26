using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// aprendizaje despues de caer en una simulacion
public sealed class MicroleccionesService(IApplicationDbContext db)
{
    public sealed record Asignada(int IdActividad, string Titulo, string Motivo, bool Completada);

    // asigna una microleccion o null si no hay una disponible
    public async Task<Asignada?> AsignarAsync(EnvioSimulacion envio, int idCategoria, CancellationToken ct = default)
    {
        var existente = await Consultar(envio.Id_Envio).FirstOrDefaultAsync(ct);

        if (existente is not null)
            return existente;

        var motivo = envio.IngresoCredenciales ? "credenciales" : "clic";

        var candidatas = await db.Set<ActividadEducativa>().AsNoTracking().Where(a => a.Habilitada && a.EsMicroleccion)
            .Select(a => new { a.Id, a.IdCategoria, a.ParaCredenciales }).ToListAsync(ct);
        var elegida = (motivo == "credenciales"
                ? candidatas.OrderByDescending(a => a.ParaCredenciales).ThenByDescending(a => a.IdCategoria == idCategoria)
                : candidatas.Where(a => a.IdCategoria == idCategoria).OrderBy(a => a.ParaCredenciales))
            .ThenBy(a => a.Id).FirstOrDefault();

        if (elegida is null)
            return null;

        var nueva = new AsignacionMicroleccion {
            IdUsuario = envio.Id_Usuario,
            IdEnvio = envio.Id_Envio,
            IdActividad = elegida.Id,
            Motivo = motivo,
            FechaAsignacion = DateTime.UtcNow,
        };
        db.Set<AsignacionMicroleccion>().Add(nueva);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.EsDuplicado())
        {
            db.Set<AsignacionMicroleccion>().Entry(nueva).State = EntityState.Detached;
        }
        return await Consultar(envio.Id_Envio).FirstOrDefaultAsync(ct);
    }

    private IQueryable<Asignada> Consultar(int idEnvio) => db.Set<AsignacionMicroleccion>().AsNoTracking().Where(a => a.IdEnvio == idEnvio)
        .Select(a => new Asignada(a.IdActividad, a.Actividad.Titulo, a.Motivo, a.FechaCompletado != null));

    // marca como completadas las asignaciones pendientes y devuelve sus ids
    public async Task<List<int>> CompletarAsync(int usuario, int idActividad, int idIntento, CancellationToken ct = default)
    {
        var pendientes = await db.Set<AsignacionMicroleccion>()
            .Where(a => a.IdUsuario == usuario && a.IdActividad == idActividad && a.FechaCompletado == null).ToListAsync(ct);

        foreach (var a in pendientes)
        {
            a.FechaCompletado = DateTime.UtcNow;
            a.IdIntento = idIntento;
        }
        return pendientes.Select(a => a.Id).ToList();
    }
}
