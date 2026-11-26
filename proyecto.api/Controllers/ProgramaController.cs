using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using proyect.application.Abstractions;
using proyect.application.Services;
using proyect.domain.Constants;
using proyecto.api.Configuration;
using Microsoft.Extensions.Configuration;

namespace proyecto.api.Controllers;

// administracion del programa: rutas, cumplimiento, plan anual, encuestas, panel y evidencias
[ApiController]
[Route("api/programa")]
[Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class ProgramaController(
    RutasAprendizajeService rutas,
    PlanificadorService plan,
    EncuestasCulturaService encuestas,
    ProgramaService programa,
    IAuditoriaService auditoria,
    RecordatoriosProgramadosService programados,
    IConfiguration configuration) : ControllerBase
{
    private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public sealed record EstadoEncuestaRequest(string Estado);

    [HttpGet("rutas")]
    public async Task<IActionResult> Rutas(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        Ok(await rutas.ListarAsync(ct, pagina, tamanio));

    [HttpPost("rutas")]
    public async Task<IActionResult> CrearRuta(RutaRequest r, CancellationToken ct) =>
        Ok(new { id = await rutas.GuardarAsync(null, r, Actor, ct) });

    [HttpPut("rutas/{id:int}")]
    public async Task<IActionResult> EditarRuta(int id, RutaRequest r, CancellationToken ct)
    {
        await rutas.GuardarAsync(id, r, Actor, ct);
        return NoContent();
    }

    [HttpGet("cumplimiento")]
    public async Task<IActionResult> Cumplimiento(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        Ok(await rutas.CumplimientoAsync(ct, pagina, tamanio));

    [HttpPost("recordatorios")]
    public async Task<IActionResult> Recordatorios(CancellationToken ct) => await programados.EnviarManualAsync(Actor, ct) is { } resultado
        ? Ok(resultado)
        : Conflict(new { mensaje = "Ya hay un envío de recordatorios en curso. Intenta de nuevo en unos minutos." });

    [HttpGet("recordatorios/estado")]
    public async Task<IActionResult> EstadoRecordatorios(CancellationToken ct) =>
        Ok(await programados.EstadoAsync(RecordatoriosAprendizajeWorker.Configuracion(configuration), ct));

    [HttpGet("plan/{anio:int}")]
    public async Task<IActionResult> Plan(int anio, CancellationToken ct) => Ok(await plan.AnioAsync(anio, ct));

    [HttpPost("plan")]
    public async Task<IActionResult> CrearEvento(EventoPlanRequest r, CancellationToken ct) =>
        Ok(new { id = await plan.GuardarAsync(null, r, Actor, ct) });

    [HttpPut("plan/{id:int}")]
    public async Task<IActionResult> EditarEvento(int id, EventoPlanRequest r, CancellationToken ct)
    {
        await plan.GuardarAsync(id, r, Actor, ct);
        return NoContent();
    }

    [HttpDelete("plan/{id:int}")]
    public async Task<IActionResult> EliminarEvento(int id, CancellationToken ct)
    {
        await plan.EliminarAsync(id, Actor, ct);
        return NoContent();
    }

    [HttpPost("plan/{anio:int}/temporadas")]
    public async Task<IActionResult> Temporadas(int anio, CancellationToken ct) => Ok(await plan.CargarTemporadasAsync(anio, Actor, ct));

    [HttpGet("encuestas")]
    public async Task<IActionResult> Encuestas(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        Ok(await encuestas.ListarAsync(ct, pagina, tamanio));

    [HttpGet("encuestas/preguntas-base")]
    public IActionResult PreguntasBase() => Ok(new {
        preguntas = EncuestasCulturaService.PreguntasBase,
        dimensiones = EncuestasCulturaService.Dimensiones
    });

    [HttpPost("encuestas")]
    public async Task<IActionResult> CrearEncuesta(EncuestaRequest r, CancellationToken ct) =>
        Ok(new { id = await encuestas.CrearAsync(r, Actor, ct) });

    [HttpPost("encuestas/{id:int}/estado")]
    public async Task<IActionResult> EstadoEncuesta(int id, EstadoEncuestaRequest r, CancellationToken ct)
    {
        await encuestas.CambiarEstadoAsync(id, r.Estado, Actor, ct);
        return NoContent();
    }

    [HttpGet("encuestas/{id:int}/resultados")]
    public async Task<IActionResult> ResultadosEncuesta(int id, CancellationToken ct) => Ok(await encuestas.ResultadosAsync(id, ct));

    [HttpGet("panel")]
    public async Task<IActionResult> Panel(CancellationToken ct, DateTime? desde = null, DateTime? hasta = null) =>
        Ok(await programa.PanelAsync(desde, hasta, ct));

    [HttpGet("matriz")]
    public async Task<IActionResult> Matriz(CancellationToken ct) => Ok(await programa.MatrizAsync(ct));

    [HttpGet("evidencias")]
    public async Task<IActionResult> Evidencias(CancellationToken ct, DateTime? desde = null, DateTime? hasta = null)
    {
        var paquete = await programa.EvidenciasAsync(desde, hasta, Actor, ct);
        await auditoria.RegistrarAsync("ExportarEvidenciasPrograma", true, Actor, entidad: "Programa", detalles: new { desde, hasta });

        return Ok(paquete);
    }
}

// ruta, constancias y encuesta del colaborador; verificacion publica de constancias
[ApiController]
[Route("api")]
public sealed class AprendizajeProgramaController(
    RutasAprendizajeService rutas,
    ConstanciasService constancias,
    EncuestasCulturaService encuestas) : ControllerBase
{
    private int Usuario => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("aprendizaje/mi-ruta"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> MiRuta(CancellationToken ct) => Ok(await rutas.MiRutaAsync(Usuario, ct));

    [HttpGet("aprendizaje/constancias"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> MisConstancias(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        Ok(await constancias.MisConstanciasAsync(Usuario, ct, pagina, tamanio));

    [HttpPost("aprendizaje/constancias/{idIntento:int}"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> Emitir(int idIntento, CancellationToken ct) =>
        Ok(await constancias.EmitirAsync(Usuario, idIntento, ct));

    [HttpGet("constancias/{codigo}"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> Constancia(string codigo, CancellationToken ct) =>
        Ok(await constancias.ObtenerAsync(codigo, Usuario, User.IsInRole(RolesSistemas.RolAdmin), ct));

    [HttpGet("constancias/verificar/{codigo}"), AllowAnonymous, EnableRateLimiting(SecurityHardening.PoliticaSesion)]
    public async Task<IActionResult> Verificar(string codigo, CancellationToken ct) => Ok(await constancias.VerificarAsync(codigo, ct));

    [HttpGet("aprendizaje/encuesta"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> EncuestaPendiente(CancellationToken ct) =>
        await encuestas.PendienteAsync(Usuario, ct) is { } e ? Ok(e) : NoContent();

    [HttpPost("aprendizaje/encuesta/{id:int}"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
    public async Task<IActionResult> Responder(int id, Dictionary<string, int> respuestas, CancellationToken ct)
    {
        await encuestas.ResponderAsync(Usuario, id, respuestas, ct);
        return NoContent();
    }
}
