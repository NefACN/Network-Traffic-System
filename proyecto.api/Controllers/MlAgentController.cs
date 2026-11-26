using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Entidades;
using proyecto.api.Errors;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api/internal/ml/jobs")]
[AllowAnonymous]
[ServiceFilter(typeof(MlAgentAuthorizationFilter))]
public sealed class MlAgentController(MlAgentService agent, MlService service, IApplicationDbContext db, IHttpDatasetStorage storage) : ControllerBase
{
    [HttpPost("claim")]
    public async Task<IActionResult> Claim(CancellationToken ct, Guid? executionId = null) => Ok(await agent.ClaimAsync(executionId, ct));
    [HttpGet("{id:guid}/file")]
    public async Task<IActionResult> Download(Guid id, Guid lease, CancellationToken ct)
    {
        var job = await service.FindAsync(id, ct);

        if (job.Lease != lease || job.State != "running" || job.LeaseUntil < DateTime.UtcNow || job.Kind == "live")
            return Conflict();

        return File(storage.Open(job.DatasetId), "application/octet-stream");
    }
    [HttpGet("{id:guid}/source")]
    public async Task<IActionResult> Source(Guid id, Guid lease, CancellationToken ct)
    {
        var job = await service.FindAsync(id, ct);

        if (job.Lease != lease || job.State != "running")
            return Conflict();

        var dataset = await db.Set<HttpDataset>().AsNoTracking().SingleAsync(x => x.Id == job.DatasetId, ct);

        return Ok(new { dataset.Sha256, dataset.Size });
    }
    [HttpPost("{id:guid}/batches")]
    [RequestSizeLimit(40_000_000)]
    public async Task<IActionResult> Batch(Guid id, PreparedHttpBatch batch, CancellationToken ct)
    {
        await agent.IngestAsync(id, batch, ct);
        return Ok(new { accepted = true });
    }

    [HttpPost("{id:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid id, Guid lease, CancellationToken ct)
    {
        var changed = await db.Set<MlExecution>().Where(x => x.Id == id && x.Lease == lease && x.State == "running" && x.LeaseUntil > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTime.UtcNow.AddMinutes(5)), ct);
        return changed == 1 ? Ok() : Conflict();
    }
    [HttpPost("{id:guid}/fail")]
    public async Task<IActionResult> Fail(Guid id, AgentFailureRequest request, CancellationToken ct) { await agent.FailAsync(id, request, ct); return NoContent(); }
}
