using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Abstractions;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

// CyberScore por colaborador/ lista y detalle <administrador> o distribucion agregada <gerente>
[ApiController]
[Route("api/cyberscore")]
[Authorize(Roles = RolesSistemas.RolesPreventivos)]
public sealed class CyberScoreController(CyberScoreColaboradoresService service, PlanRefuerzoService plan, IAuditoriaService auditoria) : ControllerBase
{
    private int Usuario => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("colaboradores")]
    public async Task<IActionResult> Colaboradores(CancellationToken ct, int? idSucursal = null, int? idArea = null, string? nivel = null, string? busqueda = null,
        int? pagina = null, int? tamanio = null)
        => Ok(await service.ConsultarAsync(Usuario, User.IsInRole(RolesSistemas.RolAdmin), idSucursal, idArea, nivel, busqueda, ct, pagina, tamanio));

    // nivel de CyberScore por area operativa 
    [HttpGet("areas")]
    [Authorize(Roles = RolesSistemas.RolAdmin)]
    public async Task<IActionResult> Areas(CancellationToken ct, int? idSucursal = null) => Ok(await service.PorAreaAsync(idSucursal, 0, ct));

    [HttpGet("colaboradores/{id:int}")]
    [Authorize(Roles = RolesSistemas.RolAdmin)]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct)
    {
        var detalle = await service.DetalleAsync(id, ct);
        // consultar el resultado individual de una persona queda auditado (ISO/IEC 27001 A.5.34, privacidad)
        await auditoria.RegistrarAsync("ConsultarCyberScoreIndividual", true, Usuario, entidad: "ApplicationUser", idEntidadAfectada: id.ToString());
        return Ok(detalle);
    }

    [HttpPost("colaboradores/{id:int}/plan-refuerzo")]
    [Authorize(Roles = RolesSistemas.RolAdmin)]
    public async Task<IActionResult> PlanRefuerzo(int id, CancellationToken ct)
    {
        var asignadas = await plan.EvaluarAsync(id, ct);
        await auditoria.RegistrarAsync("EvaluarPlanRefuerzo", true, Usuario, entidad: "ApplicationUser", idEntidadAfectada: id.ToString(), detalles: new { asignadas });
        return Ok(new { asignadas });
    }
}
