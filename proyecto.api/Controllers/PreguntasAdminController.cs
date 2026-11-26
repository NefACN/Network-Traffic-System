using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.application.Dtos;
using proyect.domain.Constants;
using proyect.application;
using proyect.application.Exceptions;
using System.Security.Claims;

namespace proyecto.api.Controllers
{
    [ApiController]
    [Route("api/preguntas-admin")]
    [Authorize(Roles = RolesSistemas.RolAdmin)]
    public class PreguntasAdminController : ControllerBase
    {
        private readonly IApplicationDbContext _db;
        private readonly IAuditoriaService _auditoria;
        private readonly ILogger<PreguntasAdminController> _logger;
        // id del administrador autenticado, null si el token no lo trae (401)
        private int? Actor => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public PreguntasAdminController(IApplicationDbContext db, IAuditoriaService auditoria, ILogger<PreguntasAdminController> logger)
        {
            _db = db;
            _auditoria = auditoria;
            _logger = logger;
        }

        // solo los fallos inesperados se convierten en 500, el resto va al manejador global
        private static bool EsFalloInesperado(Exception error) => error is not (BusinessRuleException or DbUpdateException or OperationCanceledException);

        // banco de preguntas paginado
        [HttpGet]
        public async Task<ActionResult<ResultadoPaginado<PreguntaAdminDto>>> ListarPreguntas([FromQuery] int? pagina, [FromQuery] int? tamanio, CancellationToken ct)
            => Ok(await ConsultaPreguntas().PaginarAsync(pagina, tamanio, ct));

        // banco completo sin paginar para el selector de preguntas de las actividades
        [HttpGet("catalogo")]
        public async Task<ActionResult<List<PreguntaAdminDto>>> CatalogoPreguntas(CancellationToken ct)
            => Ok(await ConsultaPreguntas().ToListAsync(ct));

        private IQueryable<PreguntaAdminDto> ConsultaPreguntas() => _db.PregutasConcientizacion
            .AsNoTracking()
            .OrderBy(p => p.Id_Pregunta)
            .Select(p => new PreguntaAdminDto(
                p.Id_Pregunta,
                p.Categoria.Nombre,
                p.Id_Categoria,
                p.TextoPregunta,
                p.ExplicacionEducativa,
                p.Opciones.OrderBy(o => o.Id_Opcion).Select(o => new OpcionAdminDto(o.Id_Opcion, o.TextoOpcion, o.EsCorrecta)).ToList()));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> ObtenerPregunta(int id)
        {
            var pregunta = await _db.PregutasConcientizacion
                .Include(p => p.Opciones)
                .AsNoTracking()
                .Where(p => p.Id_Pregunta == id)
                .Select(p => new {
                    idPregunta = p.Id_Pregunta,
                    idCategoria = p.Id_Categoria,
                    textoPregunta = p.TextoPregunta,
                    explicacionEducativa = p.ExplicacionEducativa,
                    opciones = p.Opciones.Select(o => new {
                        idOpcion = o.Id_Opcion,
                        textoOpcion = o.TextoOpcion,
                        esCorrecta = o.EsCorrecta
                    }).ToList()
                })
                .FirstOrDefaultAsync();

            if (pregunta == null)
                return NotFound(new { mensaje = "Pregunta no encontrada." });

            return Ok(pregunta);
        }

        [HttpPost]
        public async Task<IActionResult> CrearPregunta([FromBody] CrearPreguntaRequest request)
        {
            if (Actor is not int actor)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.TextoPregunta))
            {
                return BadRequest(new { mensaje = "El texto de la pregunta es obligatoio" });
            }

            if (request.Opciones == null || request.Opciones.Count < 2)
            {
                return BadRequest(new { mensaje = "Proporcione al menos 2 opciones de respuesta" });
            }

            if (!request.Opciones.Any(o => o.EsCorrecta))
            {
                return BadRequest(new { mensaje = "Debe existir al menos una opcion correcta" });
            }

            var categoExistente = await _db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == request.IdCategoria);
            if (!categoExistente)
            {
                return BadRequest(new { mensaje = "La categoria no existe" });
            }

            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                var nuevaPregunta = new PreguntaConcientizacion {
                    TextoPregunta = request.TextoPregunta,
                    ExplicacionEducativa = request.ExplicacionEducativa,
                    Id_Categoria = request.IdCategoria,
                };

                _db.PregutasConcientizacion.Add(nuevaPregunta);
                await _db.SaveChangesAsync();

                var opciones = request.Opciones.Select(o => new OpcionRespuesta {
                    Id_Pregunta = nuevaPregunta.Id_Pregunta,
                    TextoOpcion = o.TextoOpcion,
                    EsCorrecta = o.EsCorrecta,
                }).ToList();

                _db.OpcionesRespuesta.AddRange(opciones);
                await _db.SaveChangesAsync();
                await _auditoria.RegistrarAsync("CrearPregunta", true, actor, entidad: "PreguntaConcientizacion", idEntidadAfectada: nuevaPregunta.Id_Pregunta.ToString());
                await transaction.CommitAsync();

                return Ok(new {
                    mensaje = "pregunta creada exitosamente",
                    idPregunta = nuevaPregunta.Id_Pregunta
                });
            }
            catch (Exception error) when (EsFalloInesperado(error))
            {
                await transaction.RollbackAsync();
                _logger.LogError(error, "No se pudo crear la pregunta.");
                return StatusCode(500, new { mensaje = "error al intentar crear la pregunta" });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> EditarPregunta(int id, [FromBody] EditarPreguntasRequest request)
        {
            if (Actor is not int actor)
                return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.TextoPregunta))
                return BadRequest(new { mensaje = "El texto de la pregunta es obligatorio." });

            if (request.Opciones == null || request.Opciones.Count < 2)
                return BadRequest(new { mensaje = "Debe proporcionar al menos 2 opciones de respuesta." });

            if (!request.Opciones.Any(o => o.EsCorrecta))
                return BadRequest(new { mensaje = "Debe existir al menos una opción correcta." });

            if (!await _db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == request.IdCategoria))
                return BadRequest(new { mensaje = "La categoría indicada no existe." });

            var pregunta = await _db.Set<PreguntaConcientizacion>()
                .Include(p => p.Opciones)
                .FirstOrDefaultAsync(p => p.Id_Pregunta == id);

            if (pregunta == null)
                return NotFound(new { mensaje = "Pregunta no encontrada." });

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                pregunta.TextoPregunta = request.TextoPregunta;
                pregunta.ExplicacionEducativa = request.ExplicacionEducativa;
                pregunta.Id_Categoria = request.IdCategoria;

                var idsOpcionesRequest = request.Opciones
                    .Where(o => o.IdOpcion.HasValue)
                    .Select(o => o.IdOpcion.GetValueOrDefault())
                    .ToList();

                var opcionesAEliminar = pregunta.Opciones
                    .Where(o => !idsOpcionesRequest.Contains(o.Id_Opcion))
                    .ToList();
                _db.OpcionesRespuesta.RemoveRange(opcionesAEliminar);

                foreach (var opcRequest in request.Opciones)
                {
                    if (opcRequest.IdOpcion.HasValue)
                    {
                        var opcionExistente = pregunta.Opciones.FirstOrDefault(o => o.Id_Opcion == opcRequest.IdOpcion.Value);
                        if (opcionExistente != null)
                        {
                            opcionExistente.TextoOpcion = opcRequest.TextoOpcion;
                            opcionExistente.EsCorrecta = opcRequest.EsCorrecta;
                        }
                    }
                    else
                    {
                        pregunta.Opciones.Add(new OpcionRespuesta {
                            TextoOpcion = opcRequest.TextoOpcion,
                            EsCorrecta = opcRequest.EsCorrecta
                        });
                    }
                }

                await _db.SaveChangesAsync();
                await _auditoria.RegistrarAsync("EditarPregunta", true, actor, entidad: "PreguntaConcientizacion", idEntidadAfectada: id.ToString());
                await transaction.CommitAsync();

                return NoContent();
            }
            catch (Exception error) when (EsFalloInesperado(error))
            {
                await transaction.RollbackAsync();
                _logger.LogError(error, "No se pudo editar la pregunta {IdPregunta}.", id);
                return StatusCode(500, new { mensaje = "Error interno al editar la pregunta." });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> EliminarPregunta(int id)
        {
            if (Actor is not int actor)
                return Unauthorized();
            var pregunta = await _db.Set<PreguntaConcientizacion>().FindAsync(id);
            if (pregunta == null)
                return NotFound(new { mensaje = "Pregunta no encontrada." });


            bool usadaEnPhishing = await _db.PreguntasAsignadasEnvio.AnyAsync(pa => pa.Id_Pregunta == id);
            bool usadaEnCurso = await _db.PreguntaAsginadasCurso.AnyAsync(pa => pa.Id_Pregunta == id);
            bool usadaEnActividad = await _db.Set<ActividadPregunta>().AnyAsync(p => p.IdPregunta == id);

            if (usadaEnPhishing || usadaEnCurso || usadaEnActividad)
            {
                return BadRequest(new {
                    mensaje = "No se puede eliminar la pregunta porque ya ha sido respondida por usuarios en campañas o cursos voluntarios. Borrarla afectaría el historial de auditoría y puntajes."
                });
            }

            try
            {
                _db.Set<PreguntaConcientizacion>().Remove(pregunta);
                await _db.SaveChangesAsync();
                await _auditoria.RegistrarAsync("EliminarPregunta", true, actor, entidad: "PreguntaConcientizacion", idEntidadAfectada: id.ToString());

                return NoContent();
            }
            // una asignacion simultanea (registro en uso) llega al manejador global como 409
            catch (Exception error) when (EsFalloInesperado(error))
            {
                _logger.LogError(error, "No se pudo eliminar la pregunta {IdPregunta}.", id);
                return StatusCode(500, new { mensaje = "Error al intentar eliminar la pregunta." });
            }
        }
    }
}
