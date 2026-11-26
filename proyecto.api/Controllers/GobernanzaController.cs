using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController, Route("api/gobernanza"), Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class GobernanzaController(GobernanzaService gobernanza) : ControllerBase
{
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("politica")]
    public async Task<IActionResult> Politica(CancellationToken ct)
    {
        var p = await gobernanza.PoliticaAsync(ct);
        return Ok(new {
            p.Revision,
            p.RequiereAprobacion,
            p.VentanaActiva,
            p.HoraInicio,
            p.HoraFin,
            p.SoloDiasHabiles,
            p.MaxSimulacionesPorPersona,
            p.UmbralAgregacion,
            pretextosProhibidos = GobernanzaService.Terminos(p),
            p.ActualizadaEn,
            ventanaAbiertaAhora = GobernanzaService.FueraDeVentana(p, DateTime.UtcNow) is null,
            motivosExclusion = GobernanzaService.MotivosExclusion,
        });
    }

    [HttpPut("politica")]
    public async Task<IActionResult> GuardarPolitica(PoliticaGobernanzaRequest request, CancellationToken ct)
    {
        await gobernanza.GuardarPoliticaAsync(request, Actor, ct);
        return NoContent();
    }

    [HttpGet("exclusiones")]
    public async Task<IActionResult> Exclusiones(CancellationToken ct, bool vigentes = true, int? pagina = null, int? tamanio = null)
        => Ok(await gobernanza.ExclusionesAsync(vigentes, ct, pagina, tamanio));

    [HttpPost("exclusiones")]
    public async Task<IActionResult> Excluir(ExclusionRequest request, CancellationToken ct) => Ok(new { id = await gobernanza.ExcluirAsync(request, Actor, ct) });

    [HttpDelete("exclusiones/{id:int}")]
    public async Task<IActionResult> Revocar(int id, CancellationToken ct)
    {
        await gobernanza.RevocarAsync(id, Actor, ct);
        return NoContent();
    }
}

[ApiController, Route("api/incidentes"), Authorize]
public sealed class IncidentesController(IncidentesService incidentes) : ControllerBase
{
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // cualquier persona con acceso al portal de aprendizaje puede reportar un mensaje real
    [HttpPost, Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(proyecto.api.Configuration.SecurityHardening.PoliticaSesion)]
    public async Task<IActionResult> Reportar(ReporteIncidenteRequest request, CancellationToken ct) => Ok(await incidentes.ReportarAsync(Actor, request, ct));

    [HttpGet("mios"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> Mios(CancellationToken ct, int? pagina = null, int? tamanio = null) => Ok(await incidentes.MisReportesAsync(Actor, ct, pagina, tamanio));

    [HttpGet, Authorize(Roles = RolesSistemas.RolAdmin)]
    public async Task<IActionResult> Listar(CancellationToken ct, string? estado = null, string? prioridad = null, int pagina = 1, int tamanio = 25)
        => Ok(await incidentes.ListarAsync(estado, prioridad, pagina, tamanio, ct));

    [HttpGet("{id:int}"), Authorize(Roles = RolesSistemas.RolAdmin)]
    public async Task<IActionResult> Detalle(int id, CancellationToken ct) => Ok(await incidentes.DetalleAsync(id, ct));

    [HttpPatch("{id:int}"), Authorize(Roles = RolesSistemas.RolAdmin)]
    public async Task<IActionResult> Actualizar(int id, ActualizarIncidenteRequest request, CancellationToken ct)
    {
        await incidentes.ActualizarAsync(id, Actor, request, ct);
        return NoContent();
    }
}
