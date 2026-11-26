using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;

namespace proyect.application.Services;

// configuracion del envio automatico (seccion Programa:Recordatorios)
public sealed record ConfiguracionRecordatorios(bool Activo = true, int HoraLocal = 9, bool SoloDiasHabiles = true, int IntervaloMinutos = 30);

// recordatorios diarios, una vez por dia con candado de PostgreSQL
public sealed class RecordatoriosProgramadosService(IApplicationDbContext db, RutasAprendizajeService rutas, IAuditoriaService auditoria)
{
    public const string AccionAutomatica = "RecordatoriosAprendizajeAutomaticos";
    private const long CandadoPostgres = 4_207_001;
    private static readonly TimeSpan Bolivia = TimeSpan.FromHours(-4);

    // inicio del dia local 
    public static DateTime InicioDiaUtc(DateTime utc) => DateTime.SpecifyKind((utc + Bolivia).Date - Bolivia, DateTimeKind.Utc);

    // corresponde ejecutar si esta activo, ya paso la hora y, si aplica, es dia habil
    public static bool CorrespondeHoy(DateTime utc, ConfiguracionRecordatorios c)
    {
        var local = utc + Bolivia;
        return c.Activo && local.Hour >= c.HoraLocal && (!c.SoloDiasHabiles || local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    public async Task<object?> EjecutarSiCorrespondeAsync(DateTime utc, ConfiguracionRecordatorios c, CancellationToken ct)
    {
        if (!CorrespondeHoy(utc, c))
            return null;
        var inicio = InicioDiaUtc(utc);
        return await ConCandadoAsync(async () =>
        {
            if (await db.RegistrosAuditoria.AnyAsync(a => a.Accion == AccionAutomatica && a.FechaHora >= inicio, ct))
                return null;
            var resultado = await rutas.EnviarRecordatoriosAsync(null, ct);
            await auditoria.RegistrarAsync(AccionAutomatica, true, null, "Sistema", entidad: "RutaAprendizaje", detalles: resultado);
            return resultado;
        }, ct);
    }

    // envio manual: comparte el candado con el automatico, null si ya hay uno en curso
    public Task<object?> EnviarManualAsync(int actor, CancellationToken ct) =>
        ConCandadoAsync(async () => (object?)await rutas.EnviarRecordatoriosAsync(actor, ct), ct);

    // candado de sesion de PostgreSQL, sin transaccion para conservar lo ya enviado
    private async Task<object?> ConCandadoAsync(Func<Task<object?>> accion, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var candado = await db.Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_lock({CandadoPostgres}) AS \"Value\"").SingleAsync(ct);
            if (!candado)
                return null;
            try
            { return await accion(); }
            finally { await db.Database.SqlQueryRaw<bool>($"SELECT pg_advisory_unlock({CandadoPostgres}) AS \"Value\"").SingleAsync(CancellationToken.None); }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public async Task<object> EstadoAsync(ConfiguracionRecordatorios c, CancellationToken ct)
    {
        var ultimo = await db.RegistrosAuditoria.AsNoTracking().Where(a => a.Accion == AccionAutomatica)
            .OrderByDescending(a => a.FechaHora).Select(a => new { a.FechaHora, a.Detalles }).FirstOrDefaultAsync(ct);
        return new { c.Activo, c.HoraLocal, c.SoloDiasHabiles, ultimaEjecucion = ultimo?.FechaHora, ultimoResultado = ultimo?.Detalles };
    }
}
