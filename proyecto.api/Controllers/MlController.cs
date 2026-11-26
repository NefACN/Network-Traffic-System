using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;
using proyect.application.Abstractions;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api")]
[Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class MlController(MlService service, MlAgentService agent, MlReportingService reports, IAuditoriaService auditoria) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<object> Registrar(string accion, string entidad, Func<Task<object>> operacion)
    {
        var result = await operacion();
        await auditoria.RegistrarAsync(accion, true, UserId, entidad: entidad, detalles: result);
        return result;
    }

    [HttpPost("http-datasets/validations")]
    [RequestSizeLimit(2_010_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2_000_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        return Accepted(await Registrar("ValidarRegistrosHttp", "HttpDataset", () => service.UploadAsync(file.FileName, content, UserId, ct)));
    }

    [HttpGet("http-datasets/validations/{id:guid}")]
    public async Task<IActionResult> Validation(Guid id, CancellationToken ct) => await Execution(id, ct);

    [HttpDelete("http-datasets/validations/{id:guid}")]
    public async Task<IActionResult> CancelValidation(Guid id, CancellationToken ct)
    {
        await service.CancelValidationAsync(id, ct);
        await auditoria.RegistrarAsync("CancelarValidacionHttp", true, UserId, entidad: "MlExecution", idEntidadAfectada: id.ToString());
        return NoContent();
    }

    [HttpPost("http-datasets")]
    public async Task<IActionResult> Confirm(ConfirmDatasetRequest request, CancellationToken ct) =>
        Ok(await Registrar("IncorporarRegistrosHttp", "HttpDataset", () => service.ConfirmAsync(request, ct)));

    [HttpGet("http-datasets")]
    public async Task<IActionResult> Datasets(CancellationToken ct, int? pagina = null, int? tamanio = null) => Ok(await service.DatasetsAsync(ct, pagina, tamanio));

    [HttpGet("ml/models")]
    public async Task<IActionResult> Models(CancellationToken ct) => Ok(await service.ModelsAsync(ct));

    [HttpPost("ml/analyses")]
    public async Task<IActionResult> Start(StartAnalysisRequest request, CancellationToken ct) =>
        Accepted(await Registrar("IniciarAnalisisHttp", "MlExecution", () => service.StartAsync(request, UserId, "historical", ct)));

    [HttpPost("ml/streams")]
    public async Task<IActionResult> Replay(StartAnalysisRequest request, CancellationToken ct) =>
        Accepted(await Registrar("IniciarReproduccionHttp", "MlExecution", () => service.StartAsync(request, UserId, "replay", ct)));

    [HttpPost("ml/sources")]
    public async Task<IActionResult> Live(LiveSourceRequest request, CancellationToken ct) =>
        Accepted(await Registrar("RegistrarFuenteHttp", "MlExecution", () => agent.RegisterLiveAsync(request, UserId, ct)));

    [HttpGet("ml/analyses")]
    public async Task<IActionResult> Executions(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        Ok(await service.ExecutionViews().PaginarAsync(pagina, tamanio, ct));

    [HttpGet("ml/analyses/{id:guid}")]
    [HttpGet("ml/streams/{id:guid}")]
    public async Task<IActionResult> Execution(Guid id, CancellationToken ct)
    {
        var result = await service.ExecutionViews(id).FirstOrDefaultAsync(ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("ml/analyses/{id:guid}/stop")]
    [HttpPost("ml/streams/{id:guid}/stop")]
    public async Task<IActionResult> Stop(Guid id, CancellationToken ct)
    {
        await service.ControlAsync(id, false, ct);
        await auditoria.RegistrarAsync("DetenerAnalisisHttp", true, UserId, entidad: "MlExecution", idEntidadAfectada: id.ToString());
        return NoContent();
    }

    [HttpPost("ml/analyses/{id:guid}/resume")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        await service.ControlAsync(id, true, ct);
        await auditoria.RegistrarAsync("ReanudarAnalisisHttp", true, UserId, entidad: "MlExecution", idEntidadAfectada: id.ToString());
        return NoContent();
    }

    [HttpGet("ml/analyses/{id:guid}/results")]
    public async Task<IActionResult> Results(
        Guid id,
        CancellationToken ct,
        int page = 1,
        int size = 25,
        int? cluster = null,
        bool? noise = null,
        bool? candidate = null,
        string? route = null,
        DateTime? from = null,
        DateTime? to = null,
        string? method = null,
        int? status = null,
        string? review = null
        )
        => Ok(await service.ResultsAsync(
            id,
            page,
            size,
            cluster,
            noise,
            candidate,
            route,
            from,
            to,
            method,
            status,
            review,
            ct)
            );

    [HttpGet("ml/analyses/{id:guid}/summary")]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct, int window = 5, DateTime? from = null, DateTime? to = null)
        => Ok(await service.SummaryAsync(id, window, from, to, ct));

    [HttpGet("ml/analyses/{id:guid}/correlations")]
    public async Task<IActionResult> Correlations(Guid id, CancellationToken ct, DateTime? from = null, DateTime? to = null)
        => Ok(await service.CorrelationsAsync(id, from, to, ct));

    [HttpGet("ml/analyses/{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, CancellationToken ct, int window = 5, DateTime? from = null, DateTime? to = null)
        => Ok(await reports.ReportAsync(id, window, from, to, ct));

    [HttpGet("ml/analyses/{id:guid}/profile")]
    public async Task<IActionResult> Profile(Guid id, CancellationToken ct, DateTime? from = null, DateTime? to = null)
        => Ok(await reports.ProfileAsync(id, from, to, ct));

    [HttpGet("ml/comparisons")]
    public async Task<IActionResult> Compare(Guid left, Guid right, CancellationToken ct, int window = 60,
        DateTime? leftFrom = null, DateTime? leftTo = null, DateTime? rightFrom = null, DateTime? rightTo = null)
        => Ok(await service.CompareAsync(left, leftFrom, leftTo, right, rightFrom, rightTo, window, ct));

    [HttpPut("ml/results/{id:long}/review")]
    public async Task<IActionResult> Review(long id, ReviewHttpRequest request, CancellationToken ct)
    {
        await service.ReviewAsync(id, request, UserId, ct);
        await auditoria.RegistrarAsync("RevisarResultadoHttp", true, UserId, entidad: "HttpAnalysisResult", idEntidadAfectada: id.ToString(),
            detalles: new { request.State }
            );
        return NoContent();
    }
}
