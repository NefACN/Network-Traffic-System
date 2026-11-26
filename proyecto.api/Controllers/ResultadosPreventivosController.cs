using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Dtos;
using proyect.application.Services;
using proyect.domain.Constants;

namespace proyecto.api.Controllers;

[ApiController]
[Route("api/resultados-campanias")]
[Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class ResultadosPreventivosController(ResultadosPreventivosService service) : ControllerBase
{
    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos(CancellationToken ct) => Ok(await service.CatalogosAsync(ct));

    [HttpGet("{id:int}/resumen")]
    public async Task<IActionResult> Resumen(int id, [FromQuery] ResultadosFiltro filtros, CancellationToken ct) => Ok(await service.ResumenAsync(id, filtros, ct));

    [HttpGet("{id:int}/participantes")]
    public async Task<IActionResult> Participantes(int id, [FromQuery] ResultadosFiltro filtros, CancellationToken ct, int pagina = 1, int tamanio = 25)
        => Ok(await service.ParticipantesAsync(id, filtros, pagina, tamanio, ct));

    [HttpGet("{id:int}/participantes/{envio:int}")]
    public async Task<IActionResult> Individual(int id, int envio, CancellationToken ct) => Ok(await service.IndividualAsync(id, envio, ct));

    [HttpGet("{id:int}/interacciones")]
    public async Task<IActionResult> Interacciones(int id, [FromQuery] ResultadosFiltro filtros, CancellationToken ct, string? tipo = null, int pagina = 1, int tamanio = 25)
        => Ok(await service.InteraccionesAsync(id, filtros, tipo, pagina, tamanio, ct));

    [HttpGet("comparacion")]
    public async Task<IActionResult> Comparar(int inicial, int posterior, [FromQuery] ResultadosFiltro filtros, CancellationToken ct)
        => Ok(await service.CompararAsync(inicial, posterior, filtros, ct));
}
