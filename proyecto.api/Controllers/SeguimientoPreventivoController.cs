using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api/seguimiento-preventivo")]
[Authorize(Roles = RolesSistemas.RolesPreventivos)]
public sealed class SeguimientoPreventivoController(SeguimientoPreventivoService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar(CancellationToken ct, int? idSucursal = null, int? idArea = null,
        DateTime? desde = null, DateTime? hasta = null, int? idCampania = null)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario))
            return Unauthorized();
        return Ok(await service.ConsultarAsync(usuario, User.IsInRole(RolesSistemas.RolAdmin), idSucursal,
            idArea, desde, hasta, idCampania, ct));
    }
}
