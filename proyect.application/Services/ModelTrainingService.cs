using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class ModelTrainingService(IApplicationDbContext db, IModelTrainingClient client)
{
    // fuentes de entrenamiento en vivo
    private IQueryable<MlExecution> Sources => db.Set<MlExecution>().AsNoTracking().Where(e => e.Profile == MlService.Profile && (
        (e.State == "completed" && (e.Kind == "historical" || e.Kind == "replay")
            && db.Set<HttpDataset>().Any(d => d.Id == e.DatasetId && d.Source == "file" && d.State == "ready"))
        || (e.Kind == "live" && db.Set<HttpDataset>().Any(d => d.Id == e.DatasetId && d.Source == "nginx"))));

    // las peticiones marcadas como incidente no se usan para aprender lo habitual
    private IQueryable<HttpAnalysisResult> Entrenables(Guid execution, DateTime? desde, DateTime? hasta)
    {
        var q = db.Set<HttpAnalysisResult>().AsNoTracking().Where(r => r.ExecutionId == execution && r.Features != "{}"
            && !db.Set<HttpAnalysisReview>().Any(v => v.ResultId == r.Id && v.State == "incident"));

        if (desde.HasValue)
        {
            var d = desde.Value.ToUniversalTime();
            q = q.Where(r => r.OccurredAt >= d);
        }
        if (hasta.HasValue)
        {
            var h = hasta.Value.ToUniversalTime();
            q = q.Where(r => r.OccurredAt <= h);
        }
        return q;
    }

    public async Task<object> DashboardAsync(CancellationToken ct)
    {
        await client.GetAsync("capabilities", ct);
        var jobs = await client.GetAsync("jobs", ct);
        var versions = await client.GetAsync("models", ct);

        // catalogo para el selector con datos de referencia
        var sources = await Sources.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Select(e => new {
            e.Id,
            e.CreatedAt,
            e.From,
            e.To,
            e.Kind,
            e.State,
            name = db.Set<HttpDataset>().Where(d => d.Id == e.DatasetId).Select(d => d.Name).First(),
            records = db.Set<HttpAnalysisResult>().LongCount(r => r.ExecutionId == e.Id && r.Features != "{}"),
            incidents = db.Set<HttpAnalysisResult>().LongCount(r => r.ExecutionId == e.Id && db.Set<HttpAnalysisReview>().Any(v => v.ResultId == r.Id && v.State == "incident"))
        }).ToListAsync(ct);

        return new {
            supported = true,
            sources = sources.Where(s => s.records >= 100),
            jobs,
            versions
        };
    }

    public async Task<JsonElement> StartAsync(StartModelTrainingRequest request, CancellationToken ct)
    {
        if (request.MinClusterSize is < 5 or > 2000 || request.MinSamples < 1 || request.MinSamples > Math.Min(1000, request.MinClusterSize)
            || request.MaxRecords is < 100 or > 30000 || request.Seed < 0 || !double.IsFinite(request.Threshold) || request.Threshold is < 0 or > 1)
            throw new BusinessRuleException("Parámetros de ajuste inválidos. min_samples no puede superar min_cluster_size.");

        if (request.Desde > request.Hasta)
            throw new BusinessRuleException("El inicio del periodo debe ser anterior al final.");

        var source = await Sources.SingleOrDefaultAsync(e => e.Id == request.ExecutionId, ct)
            ?? throw new BusinessRuleException("Selecciona un análisis histórico finalizado o una fuente de monitoreo en tiempo real.");

        var name = await db.Set<HttpDataset>().Where(d => d.Id == source.DatasetId).Select(d => d.Name).SingleAsync(ct);
        var periodo = request.Desde.HasValue || request.Hasta.HasValue;

        if (periodo)
        {
            var etiqueta = $" · {request.Desde?.ToUniversalTime():yyyy-MM-dd HH:mm} → {request.Hasta?.ToUniversalTime():yyyy-MM-dd HH:mm} UTC";
            name = (name.Length + etiqueta.Length > 120 ? name[..Math.Max(1, 120 - etiqueta.Length)] : name) + etiqueta;
        }

        // dos ventanas distintas de la misma fuente son ajustes distintos
        var sourceId = periodo ? $"{source.Id:N}:{request.Desde?.ToUniversalTime():yyyyMMddHHmm}-{request.Hasta?.ToUniversalTime():yyyyMMddHHmm}" : source.Id.ToString();

        // muestreo uniforme determinista y limitado en memoria, sin usar predicciones como etiquetas
        var sample = new List<(DateTime At, long Line, string Features)>(request.MaxRecords);
        var rng = new Random(request.Seed);
        long seen = 0;

        await foreach (var row in Entrenables(source.Id, request.Desde, request.Hasta)
            .OrderBy(r => r.Line).ThenBy(r => r.Id).Select(r => new { r.OccurredAt, r.Line, r.Features }).AsAsyncEnumerable().WithCancellation(ct))
        {
            seen++;
            if (sample.Count < request.MaxRecords)
                sample.Add((row.OccurredAt, row.Line, row.Features));
            else
            {
                var index = rng.NextInt64(seen);
                if (index < request.MaxRecords)
                    sample[(int)index] = (row.OccurredAt, row.Line, row.Features);
            }
        }

        if (sample.Count < 100 || sample.Count * .7 < request.MinClusterSize * 2)
            throw new BusinessRuleException(periodo
                ? $"El periodo elegido tiene {seen} peticiones aprovechables; se requieren al menos 100 y suficientes datos para dos grupos. Amplía el periodo."
                : "Se requieren al menos 100 registros preparados y suficientes datos para dos grupos de entrenamiento.");

        List<ModelTrainingRecord> records;
        try
        {
            records = sample
                .OrderBy(r => r.At)
                .ThenBy(r => r.Line)
                .Select(r => new ModelTrainingRecord(
                DateTime.SpecifyKind(r.At, DateTimeKind.Utc),
                JsonSerializer.Deserialize<HttpFeatures>(r.Features)
            ?? throw new JsonException())).ToList();
        }
        catch (JsonException)
        {
            throw new BusinessRuleException("La fuente contiene features incompatibles. Vuelve a procesar el archivo con el agente actual.");
        }

        return await client.StartAsync(new(
            sourceId,
            name,
            source.Profile,
            seen,
            new(
                request.MinClusterSize,
                request.MinSamples,
                request.Seed,
                request.Threshold),
            records),
            ct);
    }
    public Task<JsonElement> ValidateAsync(Guid id, CancellationToken ct) => client.ValidateAsync(id, ct);
    public Task<JsonElement> ActivateAsync(string version, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 120 || version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')))
            throw new BusinessRuleException("Versión inválida.");

        return client.ActivateAsync(version, ct);
    }
}
