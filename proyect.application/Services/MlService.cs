using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class MlService(
    IApplicationDbContext db,
    IHttpDatasetStorage storage,
    IModelAnalysisClient model,
    IModelRegistry registry,
    SugerenciasService sugerencias)
{
    public const string Profile = "nginx-request-v1";

    public async Task<object> UploadAsync(string filename, Stream content, int user, CancellationToken ct)
    {
        var (validationId, datasetId) = await UploadWithIdsAsync(filename, content, user, ct);
        return new { validationId, datasetId, state = "queued" };
    }

    public async Task<(Guid ValidationId, Guid DatasetId)> UploadWithIdsAsync(
        string filename,
        Stream content,
        int user,
        CancellationToken ct)
    {
        if (!new[] { ".log", ".txt" }.Contains(Path.GetExtension(filename).ToLowerInvariant()))
            throw new BusinessRuleException("Selecciona un archivo .log o .txt sin comprimir.");

        var dataset = new HttpDataset { CreatedBy = user };
        var file = await storage.SaveAsync(dataset.Id, filename, content, ct);
        dataset.FileName = file.FileName;
        dataset.Name = file.FileName;
        dataset.Size = file.Size;
        dataset.Sha256 = file.Sha256;

        var validation = new MlExecution {
            DatasetId = dataset.Id,
            Kind = "validation",
            CreatedBy = user
        };

        try
        {
            db.Set<HttpDataset>().Add(dataset);
            db.Set<MlExecution>().Add(validation);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            storage.Delete(dataset.Id);
            throw;
        }

        return (validation.Id, dataset.Id);
    }

    public async Task<object> ConfirmAsync(ConfirmDatasetRequest request, CancellationToken ct)
    {
        var validation = await FindAsync(request.ValidationId, ct);

        if (validation.Kind != "validation" || validation.State != "completed" || validation.Valid == 0)
            throw new BusinessRuleException("La validación debe finalizar con registros válidos antes de confirmar.");

        var dataset = await db.Set<HttpDataset>().SingleAsync(x => x.Id == validation.DatasetId, ct);

        if (dataset.State != "validated")
            throw new BusinessRuleException("El conjunto ya fue confirmado o cancelado.", BusinessError.Conflict);

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Origin))
            throw new BusinessRuleException("Indica nombre y origen.");

        if (request.IdSucursal.HasValue && !await db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal, ct))
            throw new BusinessRuleException("La sucursal seleccionada no existe.");

        dataset.Name = request.Name.Trim();
        dataset.Origin = request.Origin.Trim();
        dataset.State = "ready";
        dataset.IdSucursal = request.IdSucursal ?? dataset.IdSucursal;

        await db.SaveChangesAsync(ct);
        return new { dataset.Id, dataset.State };
    }

    public async Task CancelValidationAsync(Guid id, CancellationToken ct)
    {
        var job = await FindAsync(id, ct);

        if (job.Kind != "validation")
            throw new BusinessRuleException("No es una validación.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Set<MlExecution>()
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, "canceled")
                .SetProperty(x => x.Lease, (Guid?)null), ct);

        var dataset = await db.Set<HttpDataset>().SingleAsync(x => x.Id == job.DatasetId, ct);

        if (dataset.State == "ready")
            throw new BusinessRuleException("El conjunto ya está incorporado.");

        dataset.State = "canceled";
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        storage.Delete(dataset.Id);
    }

    public async Task<object> DatasetsAsync(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.Set<HttpDataset>().AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new {
                x.Id,
                x.Name,
                x.Origin,
                x.FileName,
                x.Size,
                x.State,
                x.Source,
                x.CreatedAt,
                x.IdSucursal,
                x.IdCampaniaSimulacion,
                sucursal = db.Sucursales.Where(s => s.Id_Sucursal == x.IdSucursal).Select(s => s.Nombre).FirstOrDefault(),
                validation = db.Set<MlExecution>()
                    .Where(j => j.DatasetId == x.Id && j.Kind == "validation")
                    .Select(j => new { j.Id, j.Read, j.Valid, j.Excluded, j.Rejected, j.Empty, j.From, j.To, j.State, j.Reasons })
                    .FirstOrDefault()
            }).PaginarAsync(pagina, tamanio, ct);

    public async Task<ModelInfo> ModelAsync(CancellationToken ct)
    {
        var info = await model.GetModelAsync(ct);
        var json = JsonSerializer.Serialize(info);
        var metadata = await registry.RegisterAsync(info, ct);
        if (JsonSerializer.Serialize(JsonSerializer.Deserialize<ModelInfo>(metadata)) != json)
            throw new BusinessRuleException("El servicio cambió la configuración sin cambiar la versión del modelo.", BusinessError.Conflict);

        return info;
    }

    public async Task<object> ModelsAsync(CancellationToken ct)
    {
        var current = await ModelAsync(ct);
        var versions = await db.Set<MlModel>().AsNoTracking().OrderByDescending(x => x.ObservedAt).ToListAsync(ct);

        return new {
            active = current,
            versions = versions.Select(x => new {
                x.Version,
                x.ObservedAt,
                metadata = JsonSerializer.Deserialize<ModelInfo>(x.Metadata)
            })
        };
    }

    public async Task<object> StartAsync(StartAnalysisRequest request, int user, string kind, CancellationToken ct)
    {
        if (request.WindowMinutes is not (5 or 15)
            || !double.IsFinite(request.Speed)
            || request.Speed < .1
            || request.Speed > 10000)
            throw new BusinessRuleException("Selecciona ventanas de 5/15 minutos y una velocidad entre 0.1 y 10000.");

        var dataset = await db.Set<HttpDataset>().FindAsync([request.DatasetId], ct)
            ?? throw new BusinessRuleException("Conjunto no encontrado.", BusinessError.NotFound);

        if (dataset.State != "ready" || dataset.Source != "file")
            throw new BusinessRuleException("Selecciona un archivo validado e incorporado.");

        var info = await ModelAsync(ct);
        var job = new MlExecution {
            DatasetId = dataset.Id,
            CreatedBy = user,
            Kind = kind,
            WindowMinutes = request.WindowMinutes,
            Speed = request.Speed,
            ModelVersion = info.Version,
            ModelMetadata = JsonSerializer.Serialize(info)
        };
        db.Set<MlExecution>().Add(job);
        await db.SaveChangesAsync(ct);

        return new { job.Id, job.State };
    }

    public async Task<MlExecution> FindAsync(Guid id, CancellationToken ct) =>
        await db.Set<MlExecution>().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new BusinessRuleException("Ejecución no encontrada.", BusinessError.NotFound);

    public IQueryable<object> ExecutionViews(Guid? id = null) => db.Set<MlExecution>().AsNoTracking()
        .Where(x => id == null || x.Id == id)
        .OrderByDescending(x => x.CreatedAt)
        .ThenByDescending(x => x.Id).Select(x => new {
            x.Id,
            x.DatasetId,
            datasetName = db.Set<HttpDataset>()
                .Where(d => d.Id == x.DatasetId)
                .Select(d => d.Name)
                .First(),
            x.Kind,
            x.State,
            x.Profile,
            x.ModelVersion,
            x.WindowMinutes,
            x.Speed,
            x.CreatedAt,
            x.UpdatedAt,
            x.FinishedAt,
            x.From,
            x.To,
            x.Read,
            x.Valid,
            x.Excluded,
            x.Rejected,
            x.Empty,
            x.Offset,
            x.LastLine,
            x.Error,
            x.Reasons,

            // la concesion vigente se renueva cada 20s
            agentConnected = x.State == "running" && x.LeaseUntil > DateTime.UtcNow,
            x.LeaseUntil,
            fileSize = db.Set<HttpDataset>().Where(d => d.Id == x.DatasetId).Select(d => d.Size).First(),
            sucursal = db.Sucursales
                .Where(s => s.Id_Sucursal == db.Set<HttpDataset>()
                    .Where(d => d.Id == x.DatasetId)
                    .Select(d => d.IdSucursal)
                    .FirstOrDefault())
                .Select(s => s.Nombre)
                .FirstOrDefault(),

            noise = db.Set<HttpAnalysisResult>().LongCount(r => r.ExecutionId == x.Id && r.Noise),
            candidates = db.Set<HttpAnalysisResult>().LongCount(r => r.ExecutionId == x.Id && r.Noise && r.Candidate)
        });

    public async Task ControlAsync(Guid id, bool resume, CancellationToken ct)
    {
        var job = await FindAsync(id, ct);
        if (resume && job.State is not ("failed" or "stopped"))
            throw new BusinessRuleException("Solo puedes reanudar ejecuciones detenidas o fallidas.");

        if (!resume && job.State is not ("queued" or "running"))
            throw new BusinessRuleException("La ejecución no está en curso.");

        if (job.Kind == "validation")
            throw new BusinessRuleException("Utiliza la cancelación de validación.");

        await db.Set<MlExecution>().Where(x => x.Id == id && x.State == job.State).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.State, resume ? "queued" : "stopped")
            .SetProperty(x => x.Error, (string?)null)
            .SetProperty(x => x.Lease, (Guid?)null)
            .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<object> ResultsAsync(Guid id, int page, int size, int? cluster, bool? noise, bool? candidate,
        string? route, DateTime? from, DateTime? to, string? method, int? status, string? review, CancellationToken ct)
    {
        await FindAsync(id, ct);
        size = Math.Clamp(size, 1, 100);

        if (page < 1 || page > int.MaxValue / size)
            throw new BusinessRuleException("Página inválida.");

        if (from > to || status is < 100 or > 599)
            throw new BusinessRuleException("Periodo o código HTTP inválido.");

        var q = db.Set<HttpAnalysisResult>().AsNoTracking().Where(x => x.ExecutionId == id);

        if (cluster.HasValue)
            q = q.Where(x => x.Cluster == cluster);
        if (noise.HasValue)
            q = q.Where(x => x.Noise == noise);
        if (candidate.HasValue)
            q = q.Where(x => x.Candidate == candidate);
        if (!string.IsNullOrWhiteSpace(route))
            q = q.Where(x => x.Route.Contains(route));
        if (!string.IsNullOrWhiteSpace(method))
            q = q.Where(x => x.Method == method);
        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);
        if (!string.IsNullOrEmpty(review))
        {
            if (!new[] { "pending", "in_review", "benign", "incident" }.Contains(review))
                throw new BusinessRuleException("Estado de revisión inválido.");

            q = review == "pending"
                ? q.Where(x => !db.Set<HttpAnalysisReview>().Any(r => r.ResultId == x.Id && r.State != "pending"))
                : q.Where(x => db.Set<HttpAnalysisReview>().Any(r => r.ResultId == x.Id && r.State == review));
        }
        if (from.HasValue)
            q = q.Where(x => x.OccurredAt >= from.Value.ToUniversalTime());
        if (to.HasValue)
            q = q.Where(x => x.OccurredAt <= to.Value.ToUniversalTime());

        var total = await q.LongCountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new {
                x.Id,
                x.Line,
                x.OccurredAt,
                x.Subject,
                x.Route,
                x.Method,
                x.Status,
                x.Cluster,
                x.Strength,
                x.AnomalyScore,
                x.Noise,
                x.Candidate,
                x.Threshold,
                x.Features,
                review = db.Set<HttpAnalysisReview>()
                    .Where(r => r.ResultId == x.Id)
                    .Select(r => new {
                        r.State,
                        r.Notes,
                        r.UpdatedAt
                    }).FirstOrDefault()
            }).ToListAsync(ct);

        return new { total, page, size, items };
    }

    // resultados de una ejecucion
    public IQueryable<HttpAnalysisResult> ResultsIn(Guid id, DateTime? from, DateTime? to)
    {
        if (from > to)
            throw new BusinessRuleException("El inicio del periodo debe ser anterior al final.");

        var q = db.Set<HttpAnalysisResult>().AsNoTracking().Where(x => x.ExecutionId == id);

        if (from.HasValue)
        {
            var f = from.Value.ToUniversalTime();
            q = q.Where(x => x.OccurredAt >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value.ToUniversalTime();
            q = q.Where(x => x.OccurredAt <= t);
        }

        return q;
    }

    public Task<object> SummaryAsync(Guid id, int minutes, CancellationToken ct) => SummaryAsync(id, minutes, null, null, ct);

    public async Task<object> SummaryAsync(Guid id, int minutes, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var job = await FindAsync(id, ct);
        if (minutes is not (5 or 15 or 60))
            throw new BusinessRuleException("Agrupaciones admitidas: 5, 15 o 60 minutos.");

        var q = ResultsIn(id, from, to);
        var groups = await q.GroupBy(x => x.Cluster).Select(g => new {
            cluster = g.Key,
            count = g.LongCount(),
            candidates = g.LongCount(x => x.Candidate),
            meanScore = g.Average(x => x.AnomalyScore),
            errorResponses = g.LongCount(x => x.Status >= 400),
            uniqueRoutes = g.Select(x => x.Route).Distinct().Count(),
            meanStrength = g.Average(x => x.Strength)
        }).ToListAsync(ct);
        var buckets = await q
            .GroupBy(x => new { Date = x.OccurredAt.Date, x.OccurredAt.Hour, Bucket = x.OccurredAt.Minute / minutes })
            .Select(g => new {
                g.Key.Date,
                g.Key.Hour,
                g.Key.Bucket,
                count = g.LongCount(),
                noise = g.LongCount(x => x.Noise),
                candidates = g.LongCount(x => x.Candidate)
            }).ToListAsync(ct);

        // periodo realmente cubierto por los resultados seleccionados (indice ExecutionId, OccurredAt)
        var periodFrom = await q.MinAsync(x => (DateTime?)x.OccurredAt, ct);
        var periodTo = await q.MaxAsync(x => (DateTime?)x.OccurredAt, ct);

        return new {
            job.Id,
            job.ModelVersion,
            job.Profile,
            job.Kind,
            job.State,
            windowMinutes = minutes,
            datasetName = await db.Set<HttpDataset>()
                .Where(d => d.Id == job.DatasetId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct),
            requestedFrom = from?.ToUniversalTime(),
            requestedTo = to?.ToUniversalTime(),
            periodFrom = periodFrom is null ? (DateTime?)null : DateTime.SpecifyKind(periodFrom.Value, DateTimeKind.Utc),
            periodTo = periodTo is null ? (DateTime?)null : DateTime.SpecifyKind(periodTo.Value, DateTimeKind.Utc),
            total = groups.Sum(x => x.count),
            noise = groups.Where(x => x.cluster == -1).Sum(x => x.count),
            candidates = groups.Sum(x => x.candidates),
            groups,
            windows = buckets
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Hour)
                .ThenBy(x => x.Bucket)
                .Select(x => new {
                    from = DateTime.SpecifyKind(x.Date.AddHours(x.Hour).AddMinutes(x.Bucket * minutes), DateTimeKind.Utc),
                    x.count,
                    x.noise,
                    x.candidates
                })
        };
    }

    public Task<object> CompareAsync(Guid leftId, Guid rightId, int window, CancellationToken ct) =>
        CompareAsync(leftId, null, null, rightId, null, null, window, ct);

    // compara dos periodos (HU-26): dos analisis o dos rangos del mismo analisis
    public async Task<object> CompareAsync(
        Guid leftId,
        DateTime? leftFrom,
        DateTime? leftTo,
        Guid rightId,
        DateTime? rightFrom,
        DateTime? rightTo,
        int window,
        CancellationToken ct)
    {
        var left = await FindAsync(leftId, ct);
        var right = await FindAsync(rightId, ct);
        var warnings = new List<string>();
        var notes = new List<string>();
        static bool Usable(MlExecution x) => x.Kind != "validation" && (x.State == "completed" || x.Kind == "live");

        if (!Usable(left) || !Usable(right))
            warnings.Add("Selecciona análisis históricos finalizados o fuentes de monitoreo.");
        if (leftId == rightId
            && (leftFrom ?? DateTime.MinValue) < (rightTo ?? DateTime.MaxValue)
            && (rightFrom ?? DateTime.MinValue) < (leftTo ?? DateTime.MaxValue))
            warnings.Add("Para comparar un mismo análisis, indica dos rangos de fechas que no se superpongan.");
        if (left.ModelVersion != right.ModelVersion
            || left.ModelMetadata != right.ModelMetadata
            || left.Profile != right.Profile)
            warnings.Add("Los periodos se analizaron con modelos o umbrales distintos; sus resultados no son comparables.");

        var a = await SummaryAsync(leftId, window, leftFrom, leftTo, ct);
        var b = await SummaryAsync(rightId, window, rightFrom, rightTo, ct);
        notes.Add("Las proporciones (%) son comparables aunque los periodos tengan distinta duración; las cantidades absolutas no.");
        if (leftId != rightId)
            notes.Add("Cada análisis empieza con contexto vacío: sesiones e historial por IP no se comparten entre periodos.");

        return new { comparable = warnings.Count == 0, warnings, notes, left = a, right = b };
    }

    public async Task ReviewAsync(long id, ReviewHttpRequest request, int user, CancellationToken ct)
    {
        if (!new[] { "pending", "in_review", "benign", "incident" }.Contains(request.State))
            throw new BusinessRuleException("Estado de revisión inválido.");
        if (request.State == "incident" && (!request.ConfirmIncident || (request.Notes?.Trim().Length ?? 0) < 10))
            throw new BusinessRuleException("Confirma la revisión manual y registra una observación de al menos 10 caracteres.");

        var result = await db.Set<HttpAnalysisResult>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new {
                x.Route,
                x.Method,
                x.Status,
                x.AnomalyScore,
                x.OccurredAt,
                sucursal = db.Set<HttpDataset>()
                    .Where(d => d.Id == db.Set<MlExecution>()
                        .Where(j => j.Id == x.ExecutionId)
                        .Select(j => j.DatasetId)
                        .First())
                    .Select(d => d.IdSucursal)
                    .FirstOrDefault()
            }).SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Resultado no encontrado.", BusinessError.NotFound);

        if (request.IdSucursal.HasValue && !await db.Sucursales.AnyAsync(s => s.Id_Sucursal == request.IdSucursal, ct))
            throw new BusinessRuleException("La sucursal no existe.");
        if (request.IdArea.HasValue && !await db.AreasOperativas.AnyAsync(a => a.Id_Area == request.IdArea, ct))
            throw new BusinessRuleException("El área no existe.");
        if (request.IdCategoria.HasValue && !await db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == request.IdCategoria, ct))
            throw new BusinessRuleException("El tema no existe.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var review = await db.Set<HttpAnalysisReview>().FindAsync([id], ct);
        var anterior = review?.State;
        if (review is null)
        {
            review = new HttpAnalysisReview { ResultId = id };
            db.Set<HttpAnalysisReview>().Add(review);
        }

        review.State = request.State;
        review.Notes = request.Notes?.Trim() ?? "";
        review.AnalystId = user;
        review.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // ciclo cerrado: un incidente confirmado genera una sugerencia para la sucursal afectada
        if (request.State == "incident" && anterior != "incident")
            await sugerencias.SugerirAsync("http", request.IdSucursal ?? result.sucursal, request.IdArea, request.IdCategoria, $"http:{id}",
                $"Anomalía HTTP confirmada: {result.Method} {result.Route} (respuesta {result.Status}, score {result.AnomalyScore:0.###})", ct);

        await tx.CommitAsync(ct);
    }

    public Task<object> CorrelationsAsync(Guid id, CancellationToken ct) => CorrelationsAsync(id, null, null, ct);

    public async Task<object> CorrelationsAsync(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await FindAsync(id, ct);

        return await ResultsIn(id, from, to)
            .Where(x => x.Noise)
            .GroupBy(x => x.Subject)
            .Where(g => g.Count() > 1)
            .Select(g => new {
                subject = g.Key,
                events = g.Count(),
                from = g.Min(x => x.OccurredAt),
                to = g.Max(x => x.OccurredAt),
                candidates = g.Count(x => x.Candidate)
            })
            .OrderByDescending(x => x.events).Take(100).ToListAsync(ct);
    }
}
