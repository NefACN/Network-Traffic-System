using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Services;

namespace proyecto.api.Controllers;

// informacion de la cuenta del usuario autenticado, para cualquier rol
[ApiController]
[Route("api/cuenta")]
[Authorize]
public sealed class CuentaController(CuentaService service) : ControllerBase
{
    [HttpGet("perfil")]
    public async Task<IActionResult> Perfil(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario))
            return Unauthorized();
        return Ok(await service.PerfilAsync(usuario, ct));
    }
}
