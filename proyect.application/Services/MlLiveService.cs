using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;

namespace proyect.application.Services;


// estado resumido de deteccion y prevencion para el canal en vivo
public sealed class MlLiveService(IApplicationDbContext db)
{
    private const int RecentAnalyses = 20, CandidateWindow = 5;

    public sealed record LiveSnapshot(
        string Version,
        int RunningAnalyses,
        long RecentCandidates,
        long Reviews,
        int PendingSuggestions,
        Guid? LatestAnalysis,
        DateTime At
        );

    public async Task<LiveSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // connected solo cambia al conectar o desconectar el agente, no en cada renovacion
        var runs = await db.Set<MlExecution>().AsNoTracking().Where(x => x.Kind != "validation")
            .OrderByDescending(x => x.CreatedAt).Take(RecentAnalyses)
            .Select(x => new {
                x.Id,
                x.State,
                x.Read,
                x.Valid,
                x.LastLine,
                x.FinishedAt,
                connected = x.State == "running" && x.LeaseUntil > now
            }).ToListAsync(ct);

        var recent = runs.Take(CandidateWindow).Select(x => x.Id).ToList();

        // las candidatas se cuentan como ruido en el indice de ejecucion
        var candidates = recent.Count == 0 ? 0 : await db.Set<HttpAnalysisResult>().AsNoTracking()
            .LongCountAsync(r => recent.Contains(r.ExecutionId) && r.Noise && r.Candidate, ct);

        var reviews = await db.Set<HttpAnalysisReview>().AsNoTracking().GroupBy(_ => 1)
            .Select(g => new {
                count = g.LongCount(),
                last = g.Max(r => r.UpdatedAt)
            }).FirstOrDefaultAsync(ct);

        var suggestions = await db.Set<SugerenciaConcientizacion>().AsNoTracking().GroupBy(_ => 1)
            .Select(g => new {
                pending = g.Count(s => s.Estado == "pendiente"),
                total = g.Count(),
                last = g.Max(s => s.UltimaEvidencia)
            }).FirstOrDefaultAsync(ct);

        var fingerprint = new StringBuilder();

        foreach (var run in runs)
            fingerprint
                .Append(run.Id)
                .Append(run.State)
                .Append(run.Read)
                .Append(run.Valid)
                .Append(run.LastLine)
                .Append(run.FinishedAt?.Ticks)
                .Append(run.connected)
                .Append('|');
        fingerprint
            .Append(candidates)
            .Append('|')
            .Append(reviews?.count)
            .Append(reviews?.last.Ticks)
            .Append('|')
            .Append(suggestions?.total)
            .Append(suggestions?.pending)
            .Append(suggestions?.last.Ticks);

        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())))[..16];
        return new LiveSnapshot(
            version,
            runs.Count(x => x.State is "running" or "queued"),
            candidates,
            reviews?.count ?? 0,
            suggestions?.pending ?? 0,
            runs.FirstOrDefault()?.Id,
            DateTime.UtcNow
            );
    }
}
