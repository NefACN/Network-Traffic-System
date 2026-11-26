using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

// ciclo cerrado entre las simulaciones de phishing y el analisis HTTP con HDBSCAN
[ApiController, Route("api/ciclo-http"), Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class CicloHttpController(SimulacionHttpService simulacion, SugerenciasService sugerencias) : ControllerBase
{
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record MicroleccionSugerenciaRequest(int IdActividad);
    public sealed record DescartarSugerenciaRequest(string? Nota);

    [HttpGet("campanias")]
    public async Task<IActionResult> Campanias(CancellationToken ct, int? pagina = null, int? tamanio = null) => Ok(await simulacion.CampaniasAsync(ct, pagina, tamanio));

    [HttpPost("campanias/{id:int}/conjunto")]
    public async Task<IActionResult> GenerarConjunto(int id, CancellationToken ct) => Accepted(await simulacion.GenerarConjuntoAsync(id, Actor, ct));

    [HttpGet("conjuntos")]
    public async Task<IActionResult> Conjuntos(CancellationToken ct, int? pagina = null, int? tamanio = null) => Ok(await simulacion.ConjuntosAsync(ct, pagina, tamanio));

    [HttpGet("ejecuciones/{id:guid}/verdad-conocida")]
    public async Task<IActionResult> VerdadConocida(Guid id, CancellationToken ct) => Ok(await simulacion.VerdadConocidaAsync(id, ct));

    [HttpGet("sugerencias")]
    public async Task<IActionResult> Sugerencias(CancellationToken ct, string? estado = null, int? pagina = null, int? tamanio = null)
        => Ok(await sugerencias.ListarAsync(estado, ct, pagina, tamanio));

    [HttpPost("sugerencias/{id:int}/campania")]
    public async Task<IActionResult> PrepararCampania(int id, CancellationToken ct) => Ok(await sugerencias.PrepararCampaniaAsync(id, Actor, ct));

    [HttpPost("sugerencias/{id:int}/microleccion")]
    public async Task<IActionResult> Microleccion(int id, MicroleccionSugerenciaRequest request, CancellationToken ct)
        => Ok(await sugerencias.AsignarMicroleccionAsync(id, request.IdActividad, Actor, ct));

    [HttpPost("sugerencias/{id:int}/descartar")]
    public async Task<IActionResult> Descartar(int id, DescartarSugerenciaRequest request, CancellationToken ct)
    {
        await sugerencias.DescartarAsync(id, request.Nota, Actor, ct);
        return NoContent();
    }
}
