using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using proyect.application.Abstractions;
using Microsoft.EntityFrameworkCore;
using proyect.application.Dtos;
using proyect.domain.Entidades;
using proyect.application.Exceptions;

namespace proyecto.api.Controllers
{
    [ApiController]
    [Route("api/concientizacion")]
    public class ConcientizacionController : ControllerBase
    {
        private readonly IApplicationDbContext _db;
        private readonly proyect.application.Services.GamificacionService _juego;
        private readonly proyect.application.Services.PhishingResultadoService _resultados;
        private readonly proyect.application.Services.MicroleccionesService _microlecciones;
        private readonly proyect.application.Services.PlanRefuerzoService _plan;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
        private readonly ILogger<ConcientizacionController> _logger;

        public ConcientizacionController(
            IApplicationDbContext db,
            proyect.application.Services.PhishingResultadoService resultados,
            proyect.application.Services.GamificacionService juego,
            proyect.application.Services.MicroleccionesService microlecciones,
            proyect.application.Services.PlanRefuerzoService plan,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
            ILogger<ConcientizacionController> logger)
        {
            _logger = logger;
            _cache = cache;
            _plan = plan;
            _db = db;
            _resultados = resultados;
            _juego = juego;
            _microlecciones = microlecciones;
        }

        // senales de alerta capturadas al iniciar la campania (NIST Phish Scale)
        private static List<SenalAlertaDto> Senales(string? registro)
        {
            if (string.IsNullOrWhiteSpace(registro))
                return [];

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(registro);
                if (!doc.RootElement.TryGetProperty("senales", out var lista))
                    return [];

                return lista.EnumerateArray()
                    .Select(x => new SenalAlertaDto(
                        x.GetProperty("tipo").GetString() ?? "",
                        x.GetProperty("descripcion").GetString() ?? "",
                        x.TryGetProperty("fragmento", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String
                            ? f.GetString()
                            : null))
                    .ToList();
            }
            catch (System.Text.Json.JsonException)
            {
                return [];
            }
        }

        [HttpGet("rastrear/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> RastrearYObtenerPreguntas(Guid token)
        {
            var envio = await _db.EnviosSimulacion
                .Include(e => e.Campania)
                    .ThenInclude(c => c.Plantilla)
                .FirstOrDefaultAsync(e => e.TokenUuid == token);

            if (envio == null)
                return NotFound(new { mensaje = "Token inválido o expirado." });

            if (!envio.HizoClick
                && (envio.EstadoEntrega == "failed"
                    || envio.Campania.Id_Estado != 2
                    || (envio.Campania.FechaFinProgramada.HasValue && envio.Campania.FechaFinProgramada.Value < DateTime.UtcNow)))
            {
                return BadRequest(new {
                    mensaje = "esta simulacion de phishing ya ha concluido, Gracias por tu interes en ciberseguridad",
                    campaniaInactiva = true,
                });
            }

            await _resultados.RegistrarClickAsync(token);

            //  microleccion breve asociada a lo ocurrido
            var microleccion = await _microlecciones.AsignarAsync(
                envio,
                envio.IdCategoriaRegistro ?? envio.Campania.Plantilla.Id_CategoriaPegunta);

            // CyberScore si cae a nivel Critico se asigna su plan de refuerzo 
            var planRefuerzo = await _plan.EvaluarAsync(envio.Id_Usuario);

            // bloquea el envio para que dos cargas simultaneas no asignen dos juegos de preguntas
            await using var asignacion = await _db.Database.BeginTransactionAsync();
            await _db.EnviosSimulacion.Where(e => e.Id_Envio == envio.Id_Envio)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.PuntosRecuperados, e => e.PuntosRecuperados));
            var preguntasYaAsignadas = await _db.PreguntasAsignadasEnvio
                .Include(pa => pa.Pregunta)
                .ThenInclude(p => p.Opciones)
                .Where(pa => pa.Id_Envio == envio.Id_Envio)
                .Select(pa => pa.Pregunta)
                .ToListAsync();

            List<PreguntaConcientizacion> preguntasMostrar;

            if (preguntasYaAsignadas.Any())
            {
                preguntasMostrar = preguntasYaAsignadas;
            }
            else
            {
                int idCategoriaRequerida = envio.IdCategoriaRegistro ?? envio.Campania.Plantilla.Id_CategoriaPegunta;

                // solo preguntas respondibles con una opcion (el cuestionario usa botones de opcion unica)
                var elegibles = await _db.PregutasConcientizacion
                    .Where(p => p.Id_Categoria == idCategoriaRequerida
                        && p.Opciones.Count >= 2
                        && p.Opciones.Count(o => o.EsCorrecta) == 1)
                    .Select(p => p.Id_Pregunta)
                    .ToListAsync();

                if (elegibles.Count == 0)
                {
                    return BadRequest("No hay preguntas configuradas");
                }

                var seleccion = elegibles.OrderBy(_ => Random.Shared.Next()).Take(2).ToList();
                preguntasMostrar = await _db.PregutasConcientizacion
                    .Include(p => p.Opciones)
                    .Where(p => seleccion.Contains(p.Id_Pregunta))
                    .ToListAsync();

                foreach (var preg in preguntasMostrar)
                {
                    _db.PreguntasAsignadasEnvio.Add(new PreguntaAsignadaEnvio {
                        Id_Envio = envio.Id_Envio,
                        Id_Pregunta = preg.Id_Pregunta,
                    });
                }

                await _db.SaveChangesAsync();
            }

            await asignacion.CommitAsync();

            var preguntasDto = preguntasMostrar.Select(p => new PreguntaDto(
                p.Id_Pregunta,
                p.TextoPregunta,
                p.ExplicacionEducativa,
                p.Opciones.Select(o => new OpcionDto(o.Id_Opcion, o.TextoOpcion)).ToList()
                )).ToList();

            return Ok(new {
                cuerpoOriginal = envio.Campania.CuerpoHtmlRegistro ?? envio.Campania.Plantilla.CuerpoHtml,
                preguntas = preguntasDto,
                ingresoCredenciales = envio.IngresoCredenciales,
                asunto = envio.Campania.AsuntoRegistro ?? envio.Campania.Plantilla.AsuntoCorreo,
                senales = Senales(envio.Campania.PhishScaleRegistro).Select(x => new {
                    x.Tipo,
                    nombreTipo = proyect.application.Services.PhishScale.Tipos.GetValueOrDefault(x.Tipo, x.Tipo),
                    x.Descripcion,
                    x.Fragmento
                }),
                dificultad = proyect.application.Services.PhishScale.Etiqueta(envio.DificultadNistRegistro),
                microleccion,
                planRefuerzoAsignado = planRefuerzo,
                // inspeccion interactiva lo que debio revisar (remitente real y destino del boton)
                ensenanza = Ensenanza(envio.Campania.EnsenanzaRegistro),
                inspeccion = envio.FechaInspeccion is null
                    ? null
                    : new {
                        encontradas = envio.InspeccionEncontradas,
                        total = envio.InspeccionTotal,
                        aciertosEnlace = envio.InspeccionAciertosEnlace
                    }
            });
        }

        // fallos permitidos por envio y hora para impedir probar combinaciones
        public const int MaximoIntentosFallidos = 5;
        private static readonly TimeSpan VentanaIntentosFallidos = TimeSpan.FromHours(1);

        [HttpPost("completar/{token}")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(proyecto.api.Configuration.SecurityHardening.PoliticaAutenticacion)]
        public async Task<IActionResult> CompletarEducacion(Guid token, [FromBody] ValidarRespuestasRequest request)
        {
            var claveFallos = $"capacitacion-fallos:{token}";
            if (_cache.TryGetValue(claveFallos, out int fallosPrevios) && fallosPrevios >= MaximoIntentosFallidos)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { mensaje = "Agotaste los intentos por ahora. Repasa la explicación de cada pregunta y vuelve a intentarlo en una hora." });

            if (request.IdsOpcionesSeleccionadas == null || !request.IdsOpcionesSeleccionadas.Any())
                return BadRequest(new { mensaje = "Debe responder todas las preguntas." });

            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _db.EnviosSimulacion.Where(e => e.TokenUuid == token)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.PuntosRecuperados, e => e.PuntosRecuperados));
            var envio = await _db.EnviosSimulacion
                .Include(e => e.Usuario)
                .Include(e => e.Campania)
                .FirstOrDefaultAsync(e => e.TokenUuid == token);

            if (envio == null)
                return NotFound(new { mensaje = "Token inválido." });

            if (!envio.HizoClick)
                return BadRequest(new { mensaje = "Primero debes acceder a la actividad educativa de esta simulación." });

            if (envio.CapacitacionCompleta)
                return BadRequest(new { mensaje = "Ya has completado esta capacitación." });

            var opcionesCorrectasEsperadas = await _db.PreguntasAsignadasEnvio
                .Where(pa => pa.Id_Envio == envio.Id_Envio)
                .SelectMany(pa => pa.Pregunta.Opciones)
                .Where(o => o.EsCorrecta)
                .Select(o => o.Id_Opcion)
                .ToListAsync();

            bool esCorrecto = opcionesCorrectasEsperadas.Count > 0
                && request.IdsOpcionesSeleccionadas.Distinct().Count() == request.IdsOpcionesSeleccionadas.Count
                && request.IdsOpcionesSeleccionadas.Count == opcionesCorrectasEsperadas.Count
                && request.IdsOpcionesSeleccionadas.All(id => opcionesCorrectasEsperadas.Contains(id));

            if (!esCorrecto)
            {
                _cache.Set(claveFallos, fallosPrevios + 1, VentanaIntentosFallidos);
                return BadRequest(new { mensaje = "Algunas respuestas son incorrectas. Revisa la explicación e intenta de nuevo." });
            }

            try
            {
                envio.CapacitacionCompleta = true;
                envio.FechaCapacitacion = DateTime.UtcNow;

                await _juego.BloquearUsuarioAsync(envio.Id_Usuario);
                int puntosGanados = envio.PuntosRefuerzoConfigurados ?? (await _juego.ReglasAsync()).PuntosRefuerzo;
                envio.PuntosRecuperados = puntosGanados;
                await _db.SaveChangesAsync();
                var nombresInsigniasGanadas = await _juego.ReconocerAsync(
                    envio.Id_Usuario,
                    $"refuerzo:{envio.Id_Envio}",
                    "Capacitación de simulación",
                    puntosGanados);

                await transaction.CommitAsync();

                return Ok(new {
                    mensaje = "¡Felicidades! Has completado tu capacitación.",
                    puntosGanados = puntosGanados,
                    insigniasDesbloqueadas = nombresInsigniasGanadas
                });
            }
            // reglas de negocio y conflictos de BD van al manejador global, la transaccion se revierte
            catch (Exception error) when (error is not (BusinessRuleException or DbUpdateException or OperationCanceledException))
            {
                await transaction.RollbackAsync();
                _logger.LogError(error, "No se pudo completar la capacitación del envío {IdEnvio}.", envio.Id_Envio);
                return StatusCode(500, new { mensaje = "Error al procesar la gamificación." });
            }
        }

        private static object? Ensenanza(string? registro)
        {
            if (string.IsNullOrWhiteSpace(registro))
                return null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(registro);
                string? Texto(string nombre) =>
                    doc.RootElement.TryGetProperty(nombre, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                        ? v.GetString()
                        : null;
                var datos = new {
                    remitenteNombre = Texto("remitenteNombre"),
                    remitenteCorreo = Texto("remitenteCorreo"),
                    enlaceMostrado = Texto("enlaceMostrado")
                };

                return datos.remitenteNombre is null && datos.remitenteCorreo is null && datos.enlaceMostrado is null ? null : datos;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        // registra la inspeccion interactiva, da puntos educativos una vez por envio sin tocar CyberScore
        [HttpPost("inspeccion/{token:guid}")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(proyecto.api.Configuration.SecurityHardening.PoliticaSesion)]
        public async Task<IActionResult> RegistrarInspeccion(Guid token, [FromBody] proyect.application.Dtos.InspeccionRequest request)
        {
            if (request.Encontradas > request.Total || request.AciertosEnlace > request.PreguntasEnlace)
                return BadRequest(new { mensaje = "Resultado de inspección inválido." });

            var permitidas = new HashSet<string> { "errores", "tecnico", "visual", "lenguaje", "tacticas", "remitente", "enlace" };
            var omitidas = (request.Omitidas ?? []).Where(permitidas.Contains).Distinct().Take(7).ToList();

            await using var transaction = await _db.Database.BeginTransactionAsync();
            await _db.EnviosSimulacion
                .Where(e => e.TokenUuid == token)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.PuntosRecuperados, e => e.PuntosRecuperados));
            var envio = await _db.EnviosSimulacion.FirstOrDefaultAsync(e => e.TokenUuid == token);
            if (envio == null)
                return NotFound(new { mensaje = "Token inválido." });

            if (!envio.HizoClick)
                return BadRequest(new { mensaje = "Primero abre la actividad educativa de esta simulación." });

            if (envio.FechaInspeccion is not null)
                return Ok(new { yaRegistrada = true, puntosGanados = 0, insigniasDesbloqueadas = Array.Empty<string>() });

            envio.FechaInspeccion = DateTime.UtcNow;
            envio.InspeccionEncontradas = request.Encontradas;
            envio.InspeccionTotal = request.Total;
            envio.InspeccionAciertosEnlace = request.PreguntasEnlace > 0 ? request.AciertosEnlace : null;
            envio.InspeccionOmitidas = omitidas.Count > 0 ? string.Join(',', omitidas) : null;
            await _juego.BloquearUsuarioAsync(envio.Id_Usuario);

            // encontrar todas las senales sin hacer clic al azar (como maximo tantos fallos como senales)
            var completa = request.Total > 0 && request.Encontradas == request.Total && request.Fallos <= request.Total;
            var puntos = completa ? (await _juego.ReglasAsync()).PuntosInspeccion : 0;
            await _db.SaveChangesAsync();

            var logros = await _juego.ReconocerAsync(
                envio.Id_Usuario,
                $"inspeccion:{envio.Id_Envio}",
                "Encontraste todas las señales de una simulación",
                puntos);
            await transaction.CommitAsync();

            return Ok(new {
                yaRegistrada = false,
                puntosGanados = puntos,
                insigniasDesbloqueadas = logros
            });
        }
    }
}
