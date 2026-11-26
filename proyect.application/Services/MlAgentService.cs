using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class MlAgentService(IApplicationDbContext db, IModelAnalysisClient model, MlService service)
{
    public async Task<object?> ClaimAsync(Guid? executionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var q = db.Set<MlExecution>().Where(x => x.State == "queued" || (x.State == "running" && x.LeaseUntil < now));
        q = executionId.HasValue ? q.Where(x => x.Id == executionId) : q.Where(x => x.Kind != "live");
        var candidates = await q.OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(10).ToListAsync(ct);
        foreach (var id in candidates)
        {
            var lease = Guid.NewGuid();
            var changed = await db.Set<MlExecution>().Where(x => x.Id == id && (x.State == "queued" || (x.State == "running" && x.LeaseUntil < now)))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Lease, lease).SetProperty(x => x.State, "running")
                    .SetProperty(x => x.LeaseUntil, now.AddMinutes(5)).SetProperty(x => x.UpdatedAt, now), ct);
            if (changed == 0)
                continue;
            var job = await db.Set<MlExecution>().AsNoTracking().SingleAsync(x => x.Id == id, ct);
            return new {
                job.Id,
                job.DatasetId,
                job.Kind,
                job.Profile,
                job.Lease,
                job.LastLine,
                job.Offset,
                job.Checkpoint,
                job.Speed
            };
        }
        return null;
    }

    public async Task IngestAsync(Guid id, PreparedHttpBatch batch, CancellationToken ct)
    {
        if (batch.Records is null || batch.Records.Count > 256 || batch.Checkpoint is null || batch.Checkpoint.Length > 24_000_000 || batch.Reasons is null)
            throw new BusinessRuleException("Lote o checkpoint inválido.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (batch.Completed && await db.Set<MlExecution>().AsNoTracking().AnyAsync(x => x.Id == id && x.State == "completed" &&
            x.Lease == batch.Lease && x.LastLine == batch.LastLine && x.Offset == batch.Offset, ct))
            return;

        var changed = await db.Set<MlExecution>().Where(x => x.Id == id && x.Lease == batch.Lease && x.State == "running" && x.LeaseUntil > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTime.UtcNow.AddMinutes(5)), ct);

        if (changed == 0)
            throw new BusinessRuleException("La ejecución está detenida o la concesión expiró.", BusinessError.Conflict);

        var job = await db.Set<MlExecution>().SingleAsync(x => x.Id == id, ct);

        if (batch.LastLine < job.LastLine)
            throw new BusinessRuleException("Checkpoint anterior al persistido.", BusinessError.Conflict);

        if (batch.LastLine == job.LastLine && !batch.Completed)
        {
            await tx.CommitAsync(ct);
            return;
        }

        if (batch.Read != batch.LastLine || batch.Read < job.Read ||
            batch.Valid < job.Valid || batch.Excluded < job.Excluded ||
            batch.Rejected < job.Rejected || batch.Empty < job.Empty ||
            batch.Offset < 0 || batch.Read != batch.Valid + batch.Excluded + batch.Rejected + batch.Empty)
            throw new BusinessRuleException("Contadores inconsistentes.");

        if (batch.From?.Kind == DateTimeKind.Unspecified || batch.To?.Kind == DateTimeKind.Unspecified || batch.From > batch.To)
            throw new BusinessRuleException("Fechas del lote inválidas.");

        if (job.Kind != "validation")
        {
            if (batch.Valid - job.Valid != batch.Records.Count)
                throw new BusinessRuleException("Faltan resultados en el lote.");

            var previous = job.LastLine;

            foreach (var row in batch.Records)
            {
                var f = row.Features;
                if (row.Line <= previous || row.Line > batch.LastLine || row.OccurredAt.Kind != DateTimeKind.Utc || row.Subject.Length != 64 ||
                    row.Route.Length > 2048 || f.Status is < 100 or > 599 || f.Bytes < 0 || f.SessionRequests < 1 || f.Hour is < 0 or > 23 || f.Day is < 0 or > 6 ||
                    !double.IsFinite(f.ErrorRate) || f.ErrorRate is < 0 or > 1 || !double.IsFinite(f.Entropy) || f.Entropy < 0 ||
                    new[] { f.ResponseMs, f.UpstreamMs, f.IntervalMs }.Any(v => v.HasValue && (!double.IsFinite(v.Value) || v < 0)) ||
                    string.IsNullOrWhiteSpace(f.Method) || f.Method.Length > 32 || f.Method.Any(c => c is < 'A' or > 'Z'))
                    throw new BusinessRuleException("Evento o características inválidos.");
                previous = row.Line;
            }
            if (batch.Records.Count > 0)
            {
                var predictions = await model.AnalyzeAsync(batch.Records.Select(r => r.Features).ToList(), ct);
                var snapshot = JsonSerializer.Deserialize<ModelInfo>(job.ModelMetadata!)!;

                if (predictions.Count != batch.Records.Count || predictions.Any(p => p.Version != job.ModelVersion || p.Threshold != snapshot.Threshold))
                    throw new BusinessRuleException("El modelo activo cambió; no se mezclarán versiones en una ejecución.");

                for (var i = 0; i < predictions.Count; i++)
                {
                    var p = predictions[i];
                    var row = batch.Records[i];

                    if (!double.IsFinite(p.Score) || !double.IsFinite(p.Strength) || p.Strength is < 0 or > 1 || p.Cluster < -1 ||
                        p.Noise != (p.Cluster == -1) || p.Candidate != (p.Noise && p.Score > p.Threshold))
                        throw new BusinessRuleException("El servicio devolvió una predicción inconsistente.");

                    db.Set<HttpAnalysisResult>().Add(new HttpAnalysisResult {
                        ExecutionId = id,
                        Line = row.Line,
                        OccurredAt = row.OccurredAt,
                        Subject = row.Subject,
                        Route = row.Route,
                        Method = row.Features.Method,
                        Status = row.Features.Status,
                        Cluster = p.Cluster,
                        Strength = p.Strength,
                        AnomalyScore = p.Score,
                        Noise = p.Noise,
                        Candidate = p.Candidate,
                        Threshold = p.Threshold,
                        Features = JsonSerializer.Serialize(row.Features)
                    });
                }
            }
        }
        else if (batch.Records.Count != 0)
            throw new BusinessRuleException("La validación no almacena predicciones.");

        job.LastLine = batch.LastLine;
        job.Offset = batch.Offset;
        job.Checkpoint = batch.Checkpoint;
        job.Read = batch.Read;
        job.Valid = batch.Valid;
        job.Excluded = batch.Excluded;
        job.Rejected = batch.Rejected;
        job.Empty = batch.Empty;
        job.From = batch.From?.ToUniversalTime();
        job.To = batch.To?.ToUniversalTime();
        job.Reasons = JsonSerializer.Serialize(batch.Reasons);
        job.UpdatedAt = DateTime.UtcNow;

        if (batch.Completed)
        {
            job.State = "completed";
            job.FinishedAt = DateTime.UtcNow;
            if (job.Kind == "validation")
            {
                var dataset = await db.Set<HttpDataset>().SingleAsync(x => x.Id == job.DatasetId, ct);
                dataset.State = job.Valid > 0 ? "validated" : "rejected";
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task FailAsync(Guid id, AgentFailureRequest request, CancellationToken ct)
    {
        var message = request.Code switch {
            "file_changed" => "El archivo cambió desde su incorporación.",
            "model_unavailable" => "No se pudo contactar al servicio de inferencia. Puedes reanudar el análisis.",
            "model_changed" => "El modelo o umbral cambió. Restaura la configuración original para reanudar o crea otra ejecución.",
            _ => "El agente no pudo completar el procesamiento. Revisa su conexión y configuración."
        };
        await db.Set<MlExecution>().Where(x => x.Id == id && x.Lease == request.Lease && x.State == "running")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, "failed").SetProperty(x => x.Error, message)
                .SetProperty(x => x.Lease, (Guid?)null).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<object> RegisterLiveAsync(LiveSourceRequest request, int user, CancellationToken ct)
    {
        var info = await service.ModelAsync(ct);

        if (request.IdSucursal.HasValue && !await db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal, ct))
            throw new BusinessRuleException("La sucursal seleccionada no existe.");

        var dataset = new HttpDataset {
            Name = request.Name,
            Origin = "Agente NGINX",
            FileName = Path.GetFileName(request.FileName),
            Source = "nginx",
            State = "ready",
            CreatedBy = user,
            IdSucursal = request.IdSucursal
        };
        var job = new MlExecution { DatasetId = dataset.Id, Kind = "live", CreatedBy = user, ModelVersion = info.Version, ModelMetadata = JsonSerializer.Serialize(info) };

        db.Set<HttpDataset>().Add(dataset);
        db.Set<MlExecution>().Add(job);
        await db.SaveChangesAsync(ct);
        return new { job.Id, job.State };
    }
}
