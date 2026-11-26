using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.application.Services;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyecto.api.Controllers;

[ApiController, Route("api/concientizacion-admin"), Authorize(Roles = RolesSistemas.RolAdmin)]
public sealed class ActividadesAdminController(
    IApplicationDbContext db,
    ActividadesEducativasService actividades,
    GamificacionService juego,
    IAuditoriaService auditoria,
    CyberScoreService cyberScore) : ControllerBase
{
    private int Usuario => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // actividades paginadas para la tabla de administracion
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int? pagina, [FromQuery] int? tamanio, CancellationToken ct) =>
        Ok(await ConsultaActividades().PaginarAsync(pagina, tamanio, ct));

    // todas las actividades sin paginar, para selectores
    [HttpGet("catalogo")]
    public async Task<IActionResult> Catalogo(CancellationToken ct) => Ok(await ConsultaActividades().ToListAsync(ct));

    private IQueryable<ActividadAdminDto> ConsultaActividades() => db.Set<ActividadEducativa>()
        .AsNoTracking()
        .OrderByDescending(a => a.ActualizadaEn)
        .ThenByDescending(a => a.Id)
        .Select(a => new ActividadAdminDto(
            a.Id,
            a.Titulo,
            a.Descripcion,
            a.IdCategoria,
            a.Categoria.Nombre,
            a.Habilitada,
            a.Revision,
            a.Puntos,
            a.DiasRepeticion,
            a.PorcentajeAprobacion,
            a.PermitirRepaso,
            a.EsMicroleccion,
            a.ParaCredenciales,
            a.Secciones,
            a.Preguntas.Select(p => p.IdPregunta).ToList()));

    [HttpPost]
    public async Task<IActionResult> Crear(ActividadEducativaRequest request, CancellationToken ct)
    {
        var result = await actividades.GuardarAsync(null, request, ct);
        await auditoria.RegistrarAsync(
            "CrearActividad",
            true,
            idUsuario: Usuario,
            entidad: "ActividadEducativa",
            detalles: new { request.Titulo, request.Habilitada });

        return Ok(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, ActividadEducativaRequest request, CancellationToken ct)
    {
        var result = await actividades.GuardarAsync(id, request, ct);

        await auditoria.RegistrarAsync(
            "EditarActividad",
            true,
            idUsuario: Usuario,
            entidad: "ActividadEducativa",
            idEntidadAfectada: id.ToString(),
            detalles: new { request.Titulo, request.Habilitada }
            );

        return Ok(result);
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> Estado(int id, EstadoActividadRequest request, CancellationToken ct)
    {
        var a = await db.Set<ActividadEducativa>()
            .AsNoTracking()
            .Include(x => x.Preguntas)
            .SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new BusinessRuleException("Actividad no encontrada.", BusinessError.NotFound);

        if (!request.Habilitada)
            await db.Set<ActividadEducativa>()
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Habilitada, false)
                    .SetProperty(x => x.Revision, x => x.Revision + 1)
                    .SetProperty(x => x.ActualizadaEn, DateTime.UtcNow), ct);
        else
            await actividades.GuardarAsync(id, new ActividadEducativaRequest(
                a.Titulo,
                a.Descripcion,
                a.IdCategoria,
                JsonSerializer.Deserialize<List<SeccionEducativa>>(a.Secciones)!,
                a.Preguntas.Select(p => p.IdPregunta).ToList(),
                request.Habilitada,
                a.Puntos,
                a.DiasRepeticion,
                a.PorcentajeAprobacion,
                a.PermitirRepaso,
                a.EsMicroleccion,
                a.ParaCredenciales), ct);

        await auditoria.RegistrarAsync(
            "EstadoActividad",
            true,
            idUsuario: Usuario,
            entidad: "ActividadEducativa",
            idEntidadAfectada: id.ToString(),
            detalles: request);

        return NoContent();
    }

    [HttpGet("gamificacion")]
    public async Task<IActionResult> Gamificacion(CancellationToken ct) => Ok(new {
        reglas = await juego.ReglasAsync(ct),
        logros = await db.Insignias.AsNoTracking().OrderBy(i => i.Id_Insignia).ToListAsync(ct)
    });

    [HttpPut("reglas")]
    public async Task<IActionResult> Reglas(ReglasGamificacionRequest request, CancellationToken ct)
    {
        var regla = await db.Set<ReglasGamificacion>().SingleAsync(ct);
        regla.PuntosCursoLibre = request.PuntosCursoLibre;
        regla.DiasCursoLibre = request.DiasCursoLibre;
        regla.PuntosRefuerzo = request.PuntosRefuerzo;
        regla.PuntosReporte = request.PuntosReporte;
        regla.PuntosMicroleccion = request.PuntosMicroleccion;
        regla.PuntosInspeccion = request.PuntosInspeccion;
        regla.MetaSemanalTemas = request.MetaSemanalTemas;
        regla.PuntosPorNivel = request.PuntosPorNivel;
        regla.Revision++;

        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("ReglasGamificacion", true, idUsuario: Usuario, entidad: "Gamificacion", detalles: request);

        return NoContent();
    }

    [HttpGet("cyberscore")]
    public async Task<IActionResult> PoliticaCyberScore(CancellationToken ct) => Ok(new {
        version = CyberScoreService.Version,
        formula = CyberScoreService.Formula,
        politica = await cyberScore.PoliticaAsync(ct),
    });

    [HttpPut("cyberscore")]
    public async Task<IActionResult> GuardarCyberScore(PoliticaCyberScoreRequest request, CancellationToken ct)
    {
        await cyberScore.GuardarPoliticaAsync(request, ct);
        await auditoria.RegistrarAsync("PoliticaCyberScore", true, idUsuario: Usuario, entidad: "PoliticaCyberScore", detalles: request);

        return NoContent();
    }

    [HttpPost("logros")]
    public Task<IActionResult> CrearLogro(LogroRequest request, CancellationToken ct) => GuardarLogro(null, request, ct);

    [HttpPut("logros/{id:int}")]
    public Task<IActionResult> EditarLogro(int id, LogroRequest request, CancellationToken ct) => GuardarLogro(id, request, ct);

    private async Task<IActionResult> GuardarLogro(int? id, LogroRequest request, CancellationToken ct)
    {
        if (request.TipoCondicion is not ("puntos" or "actividades" or "temas" or "semanas" or "reportes" or "cyberscore"))
            throw new BusinessRuleException("Selecciona una condición de logro válida.");

        if (request.TipoCondicion == "cyberscore" && request.Objetivo > 100)
            throw new BusinessRuleException("El CyberScore objetivo debe estar entre 1 y 100.");

        var logro = id.HasValue
            ? await db.Insignias.SingleOrDefaultAsync(i => i.Id_Insignia == id, ct)
                ?? throw new BusinessRuleException("Logro no encontrado.", BusinessError.NotFound)
            : new Insignia();

        if (!id.HasValue)
            db.Insignias.Add(logro);

        logro.Nombre = request.Nombre.Trim();
        logro.Descripcion = request.Descripcion.Trim();
        logro.TipoCondicion = request.TipoCondicion;
        logro.PuntosRequeridos = request.Objetivo;
        logro.Activa = request.Activa;

        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync(
            "GuardarLogro",
            true,
            idUsuario: Usuario,
            entidad: "Insignia",
            idEntidadAfectada: logro.Id_Insignia.ToString(),
            detalles: request
            );

        return Ok(new { id = logro.Id_Insignia });
    }
}

[ApiController, Route("api/aprendizaje"), Authorize(Roles = RolesSistemas.RolesAprendizaje)]
public sealed class AprendizajeController(ActividadesEducativasService actividades, GamificacionService juego) : ControllerBase
{
    private int Usuario => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("actividades")]
    public async Task<IActionResult> Disponibles(CancellationToken ct) => Ok(await actividades.DisponiblesAsync(Usuario, ct));

    [HttpPost("actividades/{id:int}/iniciar")]
    public async Task<IActionResult> Iniciar(int id, CancellationToken ct) => Ok(await actividades.IniciarAsync(id, Usuario, ct));

    [HttpGet("intentos/{id:int}")]
    public async Task<IActionResult> Intento(int id, CancellationToken ct) => Ok(await actividades.IntentoAsync(id, Usuario, ct));

    [HttpPost("intentos/{id:int}/cuestionario")]
    public async Task<IActionResult> Cuestionario(int id, CancellationToken ct) => Ok(await actividades.CuestionarioAsync(id, Usuario, ct));

    [HttpPost("intentos/{id:int}/evaluar")]
    public async Task<IActionResult> Evaluar(int id, ValidarRespuestasRequest request, CancellationToken ct) =>
        Ok(await actividades.EvaluarAsync(id, Usuario, request, ct));

    [HttpGet("intentos/{id:int}/resultado")]
    public async Task<IActionResult> Resultado(int id, CancellationToken ct) => Ok(await actividades.ResultadoAsync(id, Usuario, ct));

    [HttpPost("intentos/{id:int}/finalizar")]
    public async Task<IActionResult> Finalizar(int id, CancellationToken ct) => Ok(await actividades.FinalizarAsync(id, Usuario, ct));

    [HttpGet("mi-progreso")]
    public async Task<IActionResult> Progreso(CancellationToken ct) => Ok(await juego.ProgresoAsync(Usuario, ct));
}
