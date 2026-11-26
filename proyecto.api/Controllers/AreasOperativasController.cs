using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;
using proyect.application.Abstractions;
using System.Security.Claims;

namespace proyecto.api.Controllers;

[ApiController]
[Authorize(Roles = RolesSistemas.RolAdmin)]
[Route("api/areas-operativas")]
public sealed class AreasOperativasController(AreasOperativasService service, IAuditoriaService auditoria) : ControllerBase
{
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> ListarAreas(CancellationToken ct, [FromQuery] bool? activo = null) => Ok(await service.ListarAsync(ct, activo));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerArea(int id, CancellationToken ct)
    {
        var area = await service.ObtenerAsync(id, ct);
        return area is null ? NotFound() : Ok(area);
    }

    [HttpPost]
    public async Task<IActionResult> CrearArea(CrearAreaOperativaRequest request, CancellationToken ct)
    {
        var id = await service.CrearAsync(request, ct);
        await auditoria.RegistrarAsync("CrearArea", true, Actor, entidad: "AreaOperativa", idEntidadAfectada: id.ToString());
        return CreatedAtAction(nameof(ObtenerArea), new { id }, new { IdArea = id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> ActualizarArea(int id, ActualizarAreaOperativaRequest request, CancellationToken ct)
    {
        await service.ActualizarAsync(id, request, ct);
        await auditoria.RegistrarAsync("EditarArea", true, Actor, entidad: "AreaOperativa", idEntidadAfectada: id.ToString());
        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, CambiarEstadoAreaRequest request, CancellationToken ct)
    {
        await service.CambiarEstadoAsync(id, request.Activo, ct);
        await auditoria.RegistrarAsync("EstadoArea", true, Actor, entidad: "AreaOperativa", idEntidadAfectada: id.ToString(), detalles: request);
        return NoContent();
    }
}
