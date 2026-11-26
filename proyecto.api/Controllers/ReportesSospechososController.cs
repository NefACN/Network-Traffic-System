using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api/reportes-sospechosos")]
[Authorize(Roles = RolesSistemas.RolColaborador)]
public sealed class ReportesSospechososController(ReportesSospechososService service) : ControllerBase
{
    private int Usuario => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record ConfirmarReporteRequest(bool Confirmar);
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct, int? pagina = null, int? tamanio = null) => Ok(await service.ListarAsync(Usuario, ct, pagina, tamanio));
    [HttpGet("{token:guid}")]
    public async Task<IActionResult> Consultar(Guid token, CancellationToken ct) => Ok(await service.ConsultarAsync(token, Usuario, ct));
    [HttpPost("{token:guid}")]
    public async Task<IActionResult> Reportar(Guid token, ConfirmarReporteRequest request, CancellationToken ct)
        => Ok(await service.ReportarAsync(token, Usuario, request.Confirmar, ct));
}
