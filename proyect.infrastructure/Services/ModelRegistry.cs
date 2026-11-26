using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.domain.Entidades;
using proyect.infrastructure.Persistence;

namespace proyect.infrastructure.Services;

public sealed class ModelRegistry(ApplicationDbContext db) : IModelRegistry
{
    public async Task<string> RegisterAsync(ModelInfo info, CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(info);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ml_modelos (\"Version\", \"Metadata\", \"ObservedAt\") VALUES ({info.Version}, CAST({metadata} AS jsonb), {DateTime.UtcNow}) ON CONFLICT (\"Version\") DO NOTHING", ct);
        return await db.Set<MlModel>().AsNoTracking().Where(x => x.Version == info.Version).Select(x => x.Metadata).SingleAsync(ct);
    }
}
