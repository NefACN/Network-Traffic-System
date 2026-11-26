using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController, Route("api/ml/training"), Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class ModelTrainingController(ModelTrainingService service, IAuditoriaService audit) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await service.DashboardAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Start(StartModelTrainingRequest request, CancellationToken ct)
    {
        var result = await service.StartAsync(request, ct);
        await audit.RegistrarAsync(
            "AjustarModeloHttp",
            true,
            UserId,
            entidad: "MlTraining",
            detalles: new {
                request.ExecutionId,
                request.Desde,
                request.Hasta,
                request.MinClusterSize,
                request.MinSamples,
                request.MaxRecords,
                request.Seed,
                request.Threshold,
                jobId = result.GetProperty("id").GetString()
            });
        return Accepted(result);
    }

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
    {
        var result = await service.ValidateAsync(id, ct);
        await audit.RegistrarAsync("ValidarModeloHttp", true, UserId, entidad: "MlTraining", idEntidadAfectada: id.ToString());
        return Ok(result);
    }

    [HttpPost("models/{version}/activate")]
    public async Task<IActionResult> Activate(string version, CancellationToken ct)
    {
        var result = await service.ActivateAsync(version, ct);
        await audit.RegistrarAsync("ActivarModeloHttp", true, UserId, entidad: "MlModel", idEntidadAfectada: version);
        return Ok(result);
    }
}
