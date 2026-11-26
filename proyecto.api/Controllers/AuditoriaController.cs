using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController]
[Authorize(Roles = RolesSistemas.RolAdmin)]
[Route("api/auditoria")]
public sealed class AuditoriaController(AuditoriaConsultaService service) : ControllerBase
{
    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos(CancellationToken ct) => Ok(await service.CatalogosAsync(ct));
    [HttpGet("consulta")]
    public async Task<IActionResult> Consulta(int? idUsuario, string? accion, DateTime? desde, DateTime? hasta, CancellationToken ct, int pagina = 1, int tamanio = 25)
        => Ok(await service.PaginarAsync(idUsuario, accion, desde, hasta, pagina, tamanio, ct));
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Detalle(long id, CancellationToken ct) => Ok(await service.DetalleAsync(id, ct));
    // listado paginado acepta aun tamanioPagina
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int? idUsuario, [FromQuery] string? accion,
        [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta,
        [FromQuery] int pagina = 1, [FromQuery] int? tamanio = null, [FromQuery] int? tamanioPagina = null, CancellationToken ct = default)
        => Ok(await service.PaginarAsync(idUsuario, accion, desde, hasta, pagina,
            Math.Clamp(tamanio ?? tamanioPagina ?? proyect.application.Abstractions.Paginacion.TamanioPorDefecto, 1, proyect.application.Abstractions.Paginacion.TamanioMaximo), ct));
}
