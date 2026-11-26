using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// plan de refuerzo: al caer a nivel Critico se asignan microlecciones de los temas fallados
public sealed class PlanRefuerzoService(IApplicationDbContext db, CyberScoreService cyberScore)
{
    public const string Motivo = "critico";

    public async Task<int> EvaluarAsync(int usuario, CancellationToken ct = default)
    {
        var politica = await cyberScore.PoliticaAsync(ct);
        if (politica.MaximoPlanCritico <= 0)
            return 0;
        var resultado = await cyberScore.ResultadoAsync(usuario, ct);
        if (resultado.Nivel != "Crítico")
            return 0;

        var asignaciones = db.Set<AsignacionMicroleccion>().AsNoTracking().Where(a => a.IdUsuario == usuario);
        var pendientesPlan = await asignaciones.CountAsync(a => a.Motivo == Motivo && a.FechaCompletado == null, ct);
        var cupo = politica.MaximoPlanCritico - pendientesPlan;
        if (cupo <= 0)
            return 0;
        // no repetir lo que ya tiene pendiente ni lo que completo en los ultimos 30 dias
        var reciente = DateTime.UtcNow.AddDays(-30);
        var ocupadas = await asignaciones.Where(a => a.FechaCompletado == null || a.FechaCompletado >= reciente).Select(a => a.IdActividad).ToListAsync(ct);
        var candidatas = await db.Set<ActividadEducativa>().AsNoTracking().Where(a => a.Habilitada && a.EsMicroleccion && !ocupadas.Contains(a.Id))
            .Select(a => new { a.Id, a.IdCategoria, a.ParaCredenciales }).ToListAsync(ct);
        var temas = resultado.TemasCaidas;
        var elegidas = candidatas
            .OrderByDescending(a => temas.Contains(a.IdCategoria))
            .ThenByDescending(a => resultado.Credenciales > 0 && a.ParaCredenciales)
            .ThenBy(a => temas.IndexOf(a.IdCategoria) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(a => a.Id).Take(cupo).ToList();
        var creadas = 0;
        foreach (var actividad in elegidas)
        {
            var nueva = new AsignacionMicroleccion { IdUsuario = usuario, IdActividad = actividad.Id, Motivo = Motivo, FechaAsignacion = DateTime.UtcNow };
            db.Set<AsignacionMicroleccion>().Add(nueva);
            try
            { await db.SaveChangesAsync(ct); creadas++; }
            catch (DbUpdateException error) when (error.EsDuplicado())
            {
                // evaluacion simultanea: el indice unico conserva la primera asignacion
                db.Set<AsignacionMicroleccion>().Entry(nueva).State = EntityState.Detached;
            }
        }
        return creadas;
    }
}
