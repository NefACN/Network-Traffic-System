using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class MlReportingService(IApplicationDbContext db, MlService ml)
{
    private const int ScoreBins = 12;
    public sealed record AlertSummary(long Total, long Pending, long InReview, long Benign, long Incidents);

    // candidatas sobre el umbral sin revision se cuentan como pendientes
    private async Task<AlertSummary> AlertSummaryAsync(IQueryable<HttpAnalysisResult> q, CancellationToken ct)
    {
        var candidates = q.Where(x => x.Candidate);
        var reviewed = await (
            from r in db.Set<HttpAnalysisReview>().AsNoTracking()
            join e in candidates on r.ResultId equals e.Id
            group r by r.State into g
            select new {
                state = g.Key,
                count = g.LongCount()
            }).ToListAsync(ct);

        var total = await candidates.LongCountAsync(ct);

        long Count(string state) => reviewed.Where(x => x.state == state).Sum(x => x.count);

        return new AlertSummary(total, total - reviewed.Where(x => x.state != "pending").Sum(x => x.count),
            Count("in_review"), Count("benign"), Count("incident"));
    }


    // perfil de comportamientos atipicos para el dashboard
    public async Task<object> ProfileAsync(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var run = await ml.FindAsync(id, ct);

        if (run.Kind == "validation")
            throw new BusinessRuleException("Selecciona una ejecucion de analisis.");

        var q = ml.ResultsIn(id, from, to);
        var noise = q.Where(x => x.Noise);
        var totals = await q.GroupBy(_ => 1).Select(g => new {
            total = g.LongCount(),
            noise = g.LongCount(x => x.Noise),
            candidates = g.LongCount(x => x.Candidate)
        }).FirstOrDefaultAsync(ct);

        var threshold = await noise.MaxAsync(x => (double?)x.Threshold, ct);
        var minScore = await noise.MinAsync(x => (double?)x.AnomalyScore, ct);
        var maxScore = await noise.MaxAsync(x => (double?)x.AnomalyScore, ct);

        // el rango incluye el umbral para poder dibujarlo
        var lo = Math.Min(minScore ?? 0, threshold ?? minScore ?? 0);
        var hi = Math.Max(maxScore ?? 0, threshold ?? maxScore ?? 0);
        var span = hi > lo ? hi - lo : 1;

        var counts = minScore is null ? [] : await noise.GroupBy(x => x.AnomalyScore >= hi ? ScoreBins - 1 : (int)Math.Floor((x.AnomalyScore - lo) / span * ScoreBins))
            .Select(g => new { bin = g.Key, count = g.LongCount(), candidates = g.LongCount(x => x.Candidate) }).OrderBy(x => x.bin).ToListAsync(ct);
        var status = await noise.GroupBy(x => x.Status / 100).Select(g => new { statusClass = g.Key, count = g.LongCount() })
            .OrderBy(x => x.statusClass).ToListAsync(ct);
        var routes = await noise.GroupBy(x => x.Route).Select(g => new { route = g.Key, count = g.LongCount(), candidates = g.LongCount(x => x.Candidate) })
            .OrderByDescending(x => x.count).ThenBy(x => x.route).Take(8).ToListAsync(ct);
        var review = await AlertSummaryAsync(q, ct);

        return new {
            schema = "hdbscan-profile-v1",
            generatedAt = DateTime.UtcNow,
            total = totals?.total ?? 0,
            noise = totals?.noise ?? 0,
            candidates = totals?.candidates ?? 0,
            scores = new {
                min = lo,
                max = hi,
                bins = ScoreBins,
                threshold,
                counts
            },
            status,
            routes,
            routeLimit = 8,
            review = new {
                review.Total,
                review.Pending,
                review.InReview,
                review.Benign,
                review.Incidents
            }
        };
    }

    public Task<object> ReportAsync(Guid id, int window, CancellationToken ct) => ReportAsync(id, window, null, null, ct);

    public async Task<object> ReportAsync(Guid id, int window, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var run = await ml.FindAsync(id, ct);

        if (run.Kind == "validation")
            throw new BusinessRuleException("Selecciona una ejecucion de analisis.");

        var summary = await ml.SummaryAsync(id, window, from, to, ct);
        var q = ml.ResultsIn(id, from, to);

        var candidates = q.Where(x => x.Candidate);
        var reviews = db.Set<HttpAnalysisReview>().AsNoTracking();

        var alertSummary = await AlertSummaryAsync(q, ct);

        var alerts = await candidates
            .OrderByDescending(x => x.AnomalyScore)
            .ThenByDescending(x => x.Id).Take(50)
            .Select(x => new {
                x.Id,
                x.Line,
                x.OccurredAt,
                x.Route,
                x.Method,
                x.Status,
                x.Cluster,
                x.AnomalyScore,
                x.Threshold,
                x.Strength,
                x.Subject,
                x.Noise,
                x.Candidate,
                x.Features,
                review = reviews.Where(r => r.ResultId == x.Id).Select(r => new { r.State, r.Notes, r.UpdatedAt }).FirstOrDefault(),
                state = reviews.Where(r => r.ResultId == x.Id).Select(r => r.State).FirstOrDefault() ?? "pending"
            }).ToListAsync(ct);

        var routes = await q
            .GroupBy(x => x.Route)
            .Select(g => new {
                route = g.Key,
                count = g.LongCount(),
                candidates = g.LongCount(x => x.Candidate),
                errors = g.LongCount(x => x.Status >= 400)
            })
            .OrderByDescending(x => x.candidates)
            .ThenByDescending(x => x.count)
            .ThenBy(x => x.route)
            .Take(20)
            .ToListAsync(ct);

        var min = await q.MinAsync(x => (double?)x.AnomalyScore, ct) ?? 0;
        var max = await q.MaxAsync(x => (double?)x.AnomalyScore, ct) ?? 0;
        var span = max > min ? max - min : 1;

        var cells = await q.GroupBy(x => new {
            scoreBin = max == min ? 0 : x.AnomalyScore >= max ? 19 : (int)Math.Floor((x.AnomalyScore - min) / span * 20),
            strengthBin = x.Strength >= 1 ? 9 : (int)Math.Floor(x.Strength * 10)
        }).Select(g => new {
            g.Key.scoreBin,
            g.Key.strengthBin,
            count = g.LongCount(),
            noise = g.LongCount(x => x.Noise),
            candidates = g.LongCount(x => x.Candidate)
        }).ToListAsync(ct);

        return new {
            schema = "hdbscan-report-v1",
            startedAt,
            generatedAt = DateTime.UtcNow,
            period = new {
                from = from?.ToUniversalTime(),
                to = to?.ToUniversalTime()
            },

            execution = new {
                run.Id,
                run.DatasetId,
                run.State,
                run.Kind,
                run.ModelVersion,
                run.Profile,
                run.ModelMetadata,
                run.From,
                run.To,
                run.Read,
                run.Valid,
                run.Excluded,
                run.Rejected,
                run.Error
            },

            summary,
            alertSummary = new {
                total = alertSummary.Total,
                pending = alertSummary.Pending,
                inReview = alertSummary.InReview,
                benign = alertSummary.Benign,
                incidents = alertSummary.Incidents
            },

            alerts,
            alertLimit = 50,
            routes,
            routeLimit = 20,
            density = new {
                minScore = min,
                maxScore = max,
                scoreSpan = span,
                scoreBins = 20,
                strengthBins = 10,
                cells
            },
            notes = new[] {
                "Las alertas candidatas son ruido con score superior al umbral fijado en la ejecución. Requieren revisión humana.",
                "La fuerza es pertenencia al grupo; el score no representa probabilidad de ataque.",
                "La densidad representa conteos en el plano score/pertenencia, no la densidad multidimensional ni el árbol de HDBSCAN.",
                "Gráficas y agregados incluyen todos los resultados persistidos. Se muestran las 50 candidatas de mayor score y las 20 rutas principales.",
                "En ejecuciones activas los datos pueden avanzar durante la generación. Un reporte final requiere una ejecución finalizada.",
                "Este análisis HTTP no determina el riesgo preventivo de colaboradores o áreas operativas." }
        };
    }
}
