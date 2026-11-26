using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.application.Dtos;
using System.Security.Claims;

namespace proyecto.api.Controllers
{
    [ApiController]
    [Route("api/dashboard")]
    [Authorize(Roles = proyect.domain.Constants.RolesSistemas.RolesAprendizaje)]
    public class DashboardUsuarioController : ControllerBase
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<DashboardUsuarioController> _logger;
        private readonly proyect.application.Services.GamificacionService _juego;

        public DashboardUsuarioController(
            IApplicationDbContext db,
            ILogger<DashboardUsuarioController> logger,
            proyect.application.Services.GamificacionService juego)
        {
            _db = db;
            _logger = logger;
            _juego = juego;
        }

        [HttpGet("mi-perfil")]
        public async Task<IActionResult> ObtenerMiPerfil()
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idClaim, out int idUsuario))
                return Unauthorized();

            var perfil = await _db.Users
                .Include(u => u.Sucursal)
                .Include(u => u.LogrosObtenidos)
                    .ThenInclude(l => l.Insignia)
                .AsNoTracking()
                .Where(u => u.Id == idUsuario)
                .Select(u => new {
                    nombre = u.Nombre,
                    apellido = u.Apellido,
                    sucursal = u.Sucursal == null ? "Sin sucursal" : u.Sucursal.Nombre,
                    puntosEducativos = u.PuntosEducativos,
                    scoreRiesgo = u.ScoreRiesgo,
                    insigniasObtenidas = u.LogrosObtenidos.Select(l => new {
                        nombre = l.Insignia.Nombre,
                        icono = l.Insignia.IconoSvg,
                        fecha = l.FechaObtencion
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (perfil == null)
                return NotFound();

            return Ok(perfil);
        }

        [HttpGet("ranking-sucursales")]
        public async Task<IActionResult> ObtenerRankingSucursales()
        {
            // promedio de puntos educativos, omite sucursales bajo el umbral de agregacion
            var umbral = await _db.Set<PoliticaGobernanza>().Select(p => p.UmbralAgregacion).SingleAsync();
            var ranking = await _db.Users
                .Where(u => u.Activo && u.Sucursal != null)
                .GroupBy(u => new { u.Id_Sucursal, u.Sucursal!.Nombre })
                .Where(g => g.Count() >= umbral)
                .Select(g => new {
                    sucursal = g.Key.Nombre,
                    promedioPuntosEducativos = g.Average(u => u.PuntosEducativos),
                    totalEmpleados = g.Count()
                })
                .OrderByDescending(r => r.promedioPuntosEducativos)
                .ToListAsync();

            return Ok(ranking);
        }

        // todos los refuerzos pendientes sin paginar, la vista los pagina en el navegador
        [HttpGet("mis-refuerzos")]
        public async Task<IActionResult> MisRefuerzos()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
                return Unauthorized();

            var pendientes = await _db.EnviosSimulacion.AsNoTracking()
                .Where(e => e.Id_Usuario == idUsuario && e.HizoClick && !e.CapacitacionCompleta)
                .OrderByDescending(e => e.FechaVulneracion)
                .Select(e => new {
                    e.TokenUuid,
                    campania = e.Campania.Titulo,
                    e.FechaVulneracion,
                    tema = _db.CategoriasPregunta
                        .Where(c => c.IdCategoria == (e.IdCategoriaRegistro ?? e.Campania.Plantilla.Id_CategoriaPegunta))
                        .Select(c => c.Nombre)
                        .First()
                })
                .ToListAsync();

            return Ok(pendientes.Select(e => new {
                e.campania,
                e.tema,
                e.FechaVulneracion,
                enlace = $"phishing-educacion.html?token={e.TokenUuid}"
            }));
        }

        // historial de capacitaciones de simulaciones, paginado ({ items, total, pagina, tamanio })
        [HttpGet("mi-historial")]
        public async Task<IActionResult> ObtenerMiHistorial(int? pagina = null, int? tamanio = null, CancellationToken ct = default)
        {
            var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idClaim, out int idUsuario))
                return Unauthorized();

            var historial = await _db.EnviosSimulacion.AsNoTracking()
                .Where(e => e.Id_Usuario == idUsuario && e.CapacitacionCompleta)
                .OrderByDescending(e => e.FechaCapacitacion)
                .ThenByDescending(e => e.Id_Envio)
                .Select(e => new {
                    nombreCampania = e.Campania.Titulo,
                    temaAprendido = e.Campania.Plantilla.Nombre,
                    fechaCompletado = e.FechaCapacitacion,
                    puntosObtenidos = e.PuntosRecuperados,
                })
                .PaginarAsync(pagina, tamanio, ct);

            return Ok(historial);
        }

        [HttpGet("cursos-disponibles")]
        public async Task<IActionResult> ObtenerCursosDisponibles()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
                return Unauthorized();

            var categorias = await _db.CategoriasPregunta
                .Where(c => _db.PregutasConcientizacion.Any(p => p.Id_Categoria == c.IdCategoria
                    && p.Opciones.Count >= 2
                    && p.Opciones.Count(o => o.EsCorrecta) == 1))
                .Select(c => new {
                    idCategoria = c.IdCategoria,
                    nombre = c.Nombre,
                    ultimoExito = _db.IntentoCursoVoluntario
                        .Where(i => i.Id_Usuario == idUsuario && i.Id_Categoria == c.IdCategoria && i.Exitoso == true)
                        .OrderByDescending(i => i.FechaCompletado)
                        .Select(i => i.FechaCompletado)
                        .FirstOrDefault()
                })
                .ToListAsync();

            int diasCooldown = (await _juego.ReglasAsync()).DiasCursoLibre;

            var resultado = categorias.Select(c => new {
                c.idCategoria,
                c.nombre,
                c.ultimoExito,
                otorgaPuntos = c.ultimoExito == null || (DateTime.UtcNow - c.ultimoExito.Value).TotalDays > diasCooldown,
                mensajeCooldown = c.ultimoExito != null && (DateTime.UtcNow - c.ultimoExito.Value).TotalDays <= diasCooldown
                    ? $"Repaso habilitado. Podrás ganar puntos por esta categoría de nuevo en {diasCooldown - Math.Floor((DateTime.UtcNow - c.ultimoExito.Value).TotalDays)} días."
                    : "Practica este tema para fortalecer tus conocimientos y sumar puntos educativos."
            });

            return Ok(resultado);
        }

        [HttpPost("cursos/{idCategoria}/iniciar")]
        public async Task<IActionResult> IniciarCurso(int idCategoria)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
                return Unauthorized();

            if (!await _db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == idCategoria))
                return NotFound(new { mensaje = "La categoría no existe." });

            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _juego.BloquearUsuarioAsync(idUsuario);
            var reglas = await _juego.ReglasAsync();
            var intentoEnCurso = await _db.IntentoCursoVoluntario
                .Include(i => i.PreguntasAsignadas)
                    .ThenInclude(pa => pa.Pregunta)
                        .ThenInclude(p => p.Opciones)
                .FirstOrDefaultAsync(i => i.Id_Usuario == idUsuario
                    && i.Id_Categoria == idCategoria
                    && i.IdActividad == null
                    && i.FechaCompletado == null);

            List<PreguntaConcientizacion> preguntasMostrar = new();

            if (intentoEnCurso != null)
            {
                preguntasMostrar = intentoEnCurso.PreguntasAsignadas.Select(pa => pa.Pregunta).ToList();
            }
            else
            {
                if (!await _db.PregutasConcientizacion.AnyAsync(p => p.Id_Categoria == idCategoria
                    && p.Opciones.Count >= 2
                    && p.Opciones.Count(o => o.EsCorrecta) == 1))
                    return BadRequest("No hay preguntas para esta categoría.");

                var contexto = await _db.Users
                    .Where(u => u.Id == idUsuario)
                    .Select(u => new { u.Id_Sucursal, u.Id_Area })
                    .SingleAsync();
                intentoEnCurso = new IntentoCursoVoluntario {
                    Id_Usuario = idUsuario,
                    Id_Categoria = idCategoria,
                    FechaInicio = DateTime.UtcNow,
                    PuntosConfigurados = reglas.PuntosCursoLibre,
                    DiasRepeticionConfigurados = reglas.DiasCursoLibre,
                    ContextoRegistrado = true,
                    IdSucursalRegistro = contexto.Id_Sucursal,
                    IdAreaRegistro = contexto.Id_Area
                };
                _db.IntentoCursoVoluntario.Add(intentoEnCurso);
                await _db.SaveChangesAsync();

                int totalPreguntas = await _db.PregutasConcientizacion.CountAsync(p => p.Id_Categoria == idCategoria
                    && p.Opciones.Count >= 2
                    && p.Opciones.Count(o => o.EsCorrecta) == 1);
                if (totalPreguntas == 0)
                    return BadRequest("No hay preguntas para esta categoría.");

                Random rand = new Random();
                int limite = Math.Min(2, totalPreguntas);
                var indices = new HashSet<int>();

                while (indices.Count < limite)
                {
                    int randomIndex = rand.Next(0, totalPreguntas);
                    if (indices.Add(randomIndex))
                    {
                        var pregunta = await _db.PregutasConcientizacion
                            .Include(p => p.Opciones)
                            .Where(p => p.Id_Categoria == idCategoria
                                && p.Opciones.Count >= 2
                                && p.Opciones.Count(o => o.EsCorrecta) == 1)
                            .OrderBy(p => p.Id_Pregunta)
                            .Skip(randomIndex)
                            .Take(1)
                            .FirstOrDefaultAsync();

                        if (pregunta != null)
                        {
                            preguntasMostrar.Add(pregunta);
                            _db.PreguntaAsginadasCurso.Add(new PreguntaAsignadaCurso {
                                Id_Intento = intentoEnCurso.Id_Intento,
                                Id_Pregunta = pregunta.Id_Pregunta
                            });
                        }
                    }
                }

                await _db.SaveChangesAsync();
            }

            var preguntasDto = preguntasMostrar.Select(p => new PreguntaDto(
                p.Id_Pregunta,
                p.TextoPregunta,
                p.ExplicacionEducativa,
                p.Opciones.Select(o => new OpcionDto(o.Id_Opcion, o.TextoOpcion)).ToList()
            )).ToList();

            await transaction.CommitAsync();

            return Ok(new { idIntento = intentoEnCurso.Id_Intento, preguntas = preguntasDto });
        }

        [HttpPost("cursos/{idIntento}/completar")]
        public async Task<IActionResult> CompletarCurso(int idIntento, [FromBody] ValidarRespuestasRequest request)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
                return Unauthorized();

            if (request.IdsOpcionesSeleccionadas == null || !request.IdsOpcionesSeleccionadas.Any())
                return BadRequest(new { mensaje = "Debe responder todas las preguntas." });

            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _db.IntentoCursoVoluntario
                .Where(i => i.Id_Intento == idIntento && i.Id_Usuario == idUsuario && i.IdActividad == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PuntosObtenidos, i => i.PuntosObtenidos));
            var intento = await _db.IntentoCursoVoluntario
                .Include(i => i.Usuario)
                .Include(i => i.Categoria)
                .FirstOrDefaultAsync(i => i.Id_Intento == idIntento && i.Id_Usuario == idUsuario && i.IdActividad == null);

            if (intento == null)
                return NotFound(new { mensaje = "Intento no encontrado." });

            if (intento.FechaCompletado != null)
                return BadRequest(new { mensaje = "Este intento ya fue completado o evaluado." });

            var opcionesCorrectasEsperadas = await _db.PreguntaAsginadasCurso
                .Where(pa => pa.Id_Intento == idIntento)
                .SelectMany(pa => pa.Pregunta.Opciones)
                .Where(o => o.EsCorrecta)
                .Select(o => o.Id_Opcion)
                .ToListAsync();

            bool esCorrecto = opcionesCorrectasEsperadas.Count > 0
                && request.IdsOpcionesSeleccionadas.Distinct().Count() == request.IdsOpcionesSeleccionadas.Count
                && request.IdsOpcionesSeleccionadas.Count == opcionesCorrectasEsperadas.Count
                && request.IdsOpcionesSeleccionadas.All(id => opcionesCorrectasEsperadas.Contains(id));

            intento.FechaCompletado = DateTime.UtcNow;

            if (!esCorrecto)
            {
                intento.Exitoso = false;
                intento.PuntosObtenidos = 0;
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return BadRequest(new {
                    mensaje = "Algunas respuestas son incorrectas. Revisa la explicación.",
                    sugerencia = "Puedes iniciar un nuevo intento para recibir preguntas diferentes."
                });
            }

            intento.Exitoso = true;

            try
            {
                await _juego.BloquearUsuarioAsync(idUsuario);
                var reglas = await _juego.ReglasAsync();
                int diasCooldown = intento.DiasRepeticionConfigurados > 0 ? intento.DiasRepeticionConfigurados : reglas.DiasCursoLibre;
                var ultimoExito = await _db.IntentoCursoVoluntario
                    .Where(i => i.Id_Usuario == idUsuario
                        && i.IdActividad == null
                        && i.Id_Categoria == intento.Id_Categoria
                        && i.Exitoso == true
                        && i.Id_Intento != idIntento)
                    .MaxAsync(i => i.FechaCompletado);
                bool otorgaPuntos = ultimoExito == null || ultimoExito <= DateTime.UtcNow.AddDays(-diasCooldown);
                int puntosGanados = otorgaPuntos
                    ? (intento.DiasRepeticionConfigurados > 0 ? intento.PuntosConfigurados : reglas.PuntosCursoLibre)
                    : 0;
                intento.PuntosObtenidos = puntosGanados;
                await _db.SaveChangesAsync();
                var nombresInsigniasGanadas = await _juego.ReconocerAsync(
                    idUsuario,
                    $"curso:{idIntento}",
                    $"Práctica libre: {intento.Categoria.Nombre}",
                    puntosGanados);

                await transaction.CommitAsync();

                return Ok(new {
                    mensaje = otorgaPuntos
                        ? "¡Buen trabajo! Has completado la práctica y sumado puntos educativos."
                        : "¡Buen repaso! Mantenemos tus conocimientos frescos (Sin nuevos puntos debido al enfriamiento).",
                    puntosGanados = puntosGanados,
                    insigniasDesbloqueadas = nombresInsigniasGanadas
                });
            }
            // reglas de negocio y conflictos de BD van al manejador global, la transaccion se revierte
            catch (Exception error) when (error is not (
                proyect.application.Exceptions.BusinessRuleException or DbUpdateException or OperationCanceledException))
            {
                await transaction.RollbackAsync();
                _logger.LogError(error, "No se pudo completar el intento educativo {IdIntento}.", idIntento);
                return StatusCode(500, new { mensaje = "Error al procesar la gamificación del curso voluntario." });
            }
        }
    }
}
