using Microsoft.EntityFrameworkCore;

namespace proyect.application.Abstractions;

public static class Bloqueos
{
    public const int Incidentes = 1;
    public const int InteraccionesHttp = 2;
    public const int Encuestas = 3;
    public const int Sugerencias = 4;

    public static async Task BloquearAsync(this IApplicationDbContext db, int espacio, int clave, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("El candado transaccional requiere una transacción abierta.");
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({espacio}, {clave})", ct);
    }
}
