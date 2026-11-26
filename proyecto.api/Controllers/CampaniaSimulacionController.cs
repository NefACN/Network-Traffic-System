using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.domain.Entidades;
using proyect.application.Dtos;
using proyect.application.Security;
using proyect.domain.Constants;
using proyect.application;
using System.Security.Claims;
using System.Text.Json;
using proyect.application.Services;
using proyecto.api.Configuration;

namespace proyecto.api.Controllers
{
    [ApiController]
    [Route("api/campanias-phishing")]
    [Authorize(Roles = RolesSistemas.RolAdmin)]
    public class CampaniaSimulacionController : ControllerBase
    {
        private readonly IApplicationDbContext _db;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;
        private readonly proyect.application.Services.PhishingResultadoService _resultados;
        private readonly CampaniasService _campanias;
        private readonly IAuditoriaService _auditoria;
        private readonly PlantillasService _plantillas;
        private readonly SimulacionHttpService _simulacionHttp;

        public CampaniaSimulacionController(
            IApplicationDbContext db,
            IEmailSender emailSender,
            IConfiguration configuration,
            proyect.application.Services.PhishingResultadoService resultados,
            CampaniasService campanias,
            IAuditoriaService auditoria,
            PlantillasService plantillas,
            SimulacionHttpService simulacionHttp)
        {
            _simulacionHttp = simulacionHttp;
            _db = db;
            _emailSender = emailSender;
            _configuration = configuration;
            _resultados = resultados;
            _campanias = campanias;
            _auditoria = auditoria;
            _plantillas = plantillas;
        }

        [HttpGet("plantillas")]
        public async Task<ActionResult<ResultadoPaginado<PlantillaDto>>> ListarPlantillas(
            [FromQuery] bool? habilitada,
            [FromQuery] int? pagina,
            [FromQuery] int? tamanio,
            CancellationToken ct)
            => Ok(await _plantillas.ListarPaginaAsync(habilitada, pagina, tamanio, ct));

        // catalogo completo sin paginar para los selectores (formulario de campania)
        [HttpGet("plantillas/catalogo")]
        public async Task<ActionResult<List<PlantillaDto>>> CatalogoPlantillas([FromQuery] bool? habilitada, CancellationToken ct)
            => Ok(await _plantillas.ListarAsync(habilitada, ct));

        [HttpGet("plantillas/{id:int}")]
        public async Task<ActionResult<PlantillaDetalleDto>> ObtenerPlantilla(int id, CancellationToken ct)
            => Ok(await _plantillas.ObtenerAsync(id, ct));

        [HttpGet("phish-scale/tipos")]
        public IActionResult TiposSenales() => Ok(new {
            version = PhishScale.Version,
            tipos = PhishScale.Tipos.Select(t => new { tipo = t.Key, nombre = t.Value }),
            alineaciones = PhishScale.Alineaciones,
        });

        [HttpPut("plantillas/{id:int}")]
        public async Task<IActionResult> ActualizarPlantilla(int id, [FromBody] CrearPlantillaRequest request, CancellationToken ct)
        {
            await _plantillas.GuardarAsync(id, request, Actor, ct);
            return NoContent();
        }

        [HttpDelete("plantillas/{id:int}")]
        public async Task<IActionResult> EliminarPlantilla(int id)
        {
            var plantilla = await _db.PlantillasPhishing.FindAsync(id);
            if (plantilla == null)
                return NotFound(new { mensaje = "La plantilla no existe." });

            var vinculadaACampania = await _db.CampaniasSimulacion.AnyAsync(c => c.Id_Plantilla == id);
            if (vinculadaACampania)
            {
                return BadRequest(new { mensaje = "No se puede eliminar la plantilla porque está vinculada a una o más campañas de simulación." });
            }

            try
            {
                _db.PlantillasPhishing.Remove(plantilla);
                await _db.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateException)
            {
                return BadRequest(new { mensaje = "No se pudo eliminar la plantilla por restricciones de integridad referencial." });
            }
        }

        [HttpPost("plantillas")]
        public async Task<IActionResult> CrearPlantilla(CrearPlantillaRequest request, CancellationToken ct)
        {
            var id = await _plantillas.GuardarAsync(null, request, Actor, ct);
            return Ok(new { id, mensaje = "Plantilla creada exitosamente" });
        }

        [HttpGet("categorias-educativas")]
        [Authorize(Roles = RolesSistemas.RolAdmin)]
        public async Task<IActionResult> ObtenerCategoriasEducativas()
        {
            var categorias = await _db.CategoriasPregunta
                .AsNoTracking()
                .Select(c => new {
                    idCategoria = c.IdCategoria,
                    nombre = c.Nombre
                })
                .ToListAsync();

            return Ok(categorias);
        }

        private int Actor => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpPatch("plantillas/{id:int}/estado")]
        public async Task<IActionResult> EstadoPlantilla(int id, EstadoPlantillaRequest request)
        {
            var plantilla = await _db.PlantillasPhishing.FindAsync(id);
            if (plantilla is null)
                return NotFound(new { mensaje = "La plantilla no existe." });

            plantilla.Habilitada = request.Habilitada;
            await _db.SaveChangesAsync();
            await _auditoria.RegistrarAsync(
                "EstadoPlantilla",
                true,
                Actor,
                entidad: "PlantillaPhishing",
                idEntidadAfectada: id.ToString(),
                detalles: request);

            return NoContent();
        }

        // listado paginado de campanias, estado: pendiente (1), ejecucion (2) o finalizada
        [HttpGet]
        public async Task<ActionResult<ResultadoPaginado<CampaniaListItemDto>>> ListarCampanias(
            [FromQuery] string? estado,
            [FromQuery] int? pagina,
            [FromQuery] int? tamanio,
            CancellationToken ct)
        {
            var consulta = _db.CampaniasSimulacion.AsNoTracking().AsQueryable();
            consulta = (estado ?? "").Trim().ToLowerInvariant() switch {
                "" => consulta,
                "pendiente" => consulta.Where(c => c.Id_Estado == 1),
                "ejecucion" => consulta.Where(c => c.Id_Estado == 2),
                "finalizada" => consulta.Where(c => c.Id_Estado != 1 && c.Id_Estado != 2),
                _ => throw new proyect.application.Exceptions.BusinessRuleException(
                    "Estado de campaña no válido. Usa pendiente, ejecucion o finalizada.")
            };

            return Ok(await ProyectarCampanias(consulta).PaginarAsync(pagina, tamanio, ct));
        }

        // todas las campanias sin paginar, para selectores e indicadores
        [HttpGet("catalogo")]
        public async Task<ActionResult<List<CampaniaListItemDto>>> CatalogoCampanias(CancellationToken ct)
            => Ok(await ProyectarCampanias(_db.CampaniasSimulacion.AsNoTracking()).ToListAsync(ct));

        private IQueryable<CampaniaListItemDto> ProyectarCampanias(IQueryable<CampaniaSimulacion> consulta)
        {
            var actor = Actor;
            return consulta
                .OrderByDescending(c => c.Id_Campania)
                .Select(c => new CampaniaListItemDto(
                    c.Id_Campania,
                    c.Titulo,
                    c.Plantilla.Nombre,
                    c.Estado.NombreEstado,
                    c.FechaInicioProgramada,
                    c.FechaFinProgramada,
                    c.Segmentaciones.Count,
                    _db.Sucursales.Where(b => b.Id_Sucursal == c.IdSucursalObjetivo).Select(b => b.Nombre).FirstOrDefault(),
                    c.IdSucursalObjetivo,
                    c.Id_Estado,
                    c.Objetivo,
                    c.TipoEvaluacion,
                    c.FechaFinalizacion,
                    c.Envios.Count,
                    c.Envios.Count(e => e.EstadoEntrega == "accepted"),
                    c.Envios.Count(e => e.EstadoEntrega == "failed"),
                    c.Envios.Count(e => e.EstadoEntrega == "pending"),
                    c.EstadoAprobacion,
                    c.EstadoAprobacion == "pendiente" && c.CreadoPor != actor && c.SolicitadaPor != actor
                ));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<CampaniaDetalleDto>> ObtenerDetalle(int id)
        {
            var campania = await _db.CampaniasSimulacion
                .Include(c => c.Plantilla)
                .Include(c => c.Estado)
                .Include(c => c.Creador)
                .Include(c => c.Segmentaciones).ThenInclude(s => s.AreaOperativa)
                .Include(c => c.Envios)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id_Campania == id);

            if (campania is null)
                return NotFound();

            var detalle = new CampaniaDetalleDto(
                campania.Id_Campania,
                campania.Titulo,
                campania.Plantilla.Nombre,
                campania.Estado.NombreEstado,
                $"{campania.Creador.Nombre} {campania.Creador.Apellido}",
                campania.FechaInicioProgramada,
                campania.FechaFinProgramada,
                campania.Segmentaciones.Select(s => s.AreaOperativa.Nombre).ToList(),
                campania.Envios.Count,
                campania.Envios.Count(e => e.FueAbierto),
                campania.Envios.Count(e => e.HizoClick),
                await _db.Sucursales
                    .Where(b => b.Id_Sucursal == campania.IdSucursalObjetivo)
                    .Select(b => b.Nombre)
                    .FirstOrDefaultAsync(),
                campania.IdSucursalObjetivo,
                campania.Id_Plantilla,
                campania.Id_Estado,
                campania.Objetivo,
                campania.TipoEvaluacion,
                campania.Segmentaciones.Select(s => s.Id_Area).ToList(),
                CampaniasService.Participantes(campania),
                campania.FechaFinalizacion,
                campania.FinalizacionAnticipada,
                campania.Envios.Count(e => e.EstadoEntrega == "accepted"),
                campania.Envios.Count(e => e.EstadoEntrega == "failed"),
                campania.Envios.Count(e => e.EstadoEntrega == "pending"),
                campania.AsuntoRegistro,
                campania.CuerpoHtmlRegistro,
                campania.Envios.Count(e => e.FechaReporte != null),
                campania.Envios.Count(e => e.FechaReporte != null && e.ReporteSinClickPrevio),
                campania.NivelDificultad,
                campania.Envios.Count(e => e.IngresoCredenciales),
                campania.PaginaSenueloRegistro,
                campania.PhishScaleRegistro,
                campania.EstadoAprobacion,
                await _db.Users
                    .Where(u => u.Id == campania.SolicitadaPor)
                    .Select(u => u.Nombre + " " + u.Apellido)
                    .FirstOrDefaultAsync(),
                campania.FechaSolicitud,
                await _db.Users
                    .Where(u => u.Id == campania.RevisadaPor)
                    .Select(u => u.Nombre + " " + u.Apellido)
                    .FirstOrDefaultAsync(),
                campania.FechaRevision,
                campania.ComentarioRevision,
                campania.EstadoAprobacion == "pendiente" && campania.CreadoPor != Actor && campania.SolicitadaPor != Actor
            );

            return Ok(detalle);
        }

        [HttpPost]
        public async Task<IActionResult> CrearCampania([FromBody] CrearCampaniaRequest request)
            => Ok(new { idCampania = await _campanias.GuardarAsync(null, request, Actor) });

        [HttpPut("{id:int}")]
        public async Task<IActionResult> EditarCampania(int id, CrearCampaniaRequest request)
        {
            await _campanias.GuardarAsync(id, request, Actor);
            return NoContent();
        }

        [HttpPost("participantes")]
        public async Task<IActionResult> Participantes(ParticipantesCampaniaRequest request)
            => Ok(await _campanias.VistaParticipantesAsync(request));

        [HttpGet("{id:int}/participantes")]
        public async Task<IActionResult> EnviosParticipantes(
            int id,
            [FromQuery] int? pagina,
            [FromQuery] int? tamanio,
            CancellationToken ct)
        {
            if (!await _db.CampaniasSimulacion.AnyAsync(c => c.Id_Campania == id, ct))
                return NotFound();

            return Ok(await _db.EnviosSimulacion.AsNoTracking()
                .Where(e => e.Id_Campania == id)
                .OrderBy(e => e.Usuario.Nombre)
                .ThenBy(e => e.Usuario.Apellido)
                .ThenBy(e => e.Id_Envio)
                .Select(e => new {
                    nombre = e.Usuario.Nombre + " " + e.Usuario.Apellido,
                    correo = e.Usuario.Email,
                    e.FechaReporte,
                    e.ReporteSinClickPrevio,
                    estadoEntrega = e.EstadoEntrega,
                    fechaEnvio = e.FechaEnvio,
                    sucursal = _db.Sucursales.Where(s => s.Id_Sucursal == e.IdSucursalRegistro).Select(s => s.Nombre).FirstOrDefault(),
                    area = _db.AreasOperativas.Where(a => a.Id_Area == e.IdAreaRegistro).Select(a => a.Nombre).FirstOrDefault()
                })
                .PaginarAsync(pagina, tamanio, ct));
        }

        [HttpPost("{id:int}/solicitar-aprobacion")]
        public async Task<IActionResult> SolicitarAprobacion(int id)
        {
            await _campanias.SolicitarAprobacionAsync(id, Actor);
            return NoContent();
        }

        [HttpPost("{id:int}/revisar")]
        public async Task<IActionResult> Revisar(int id, RevisionCampaniaRequest request)
        {
            await _campanias.RevisarAsync(id, Actor, request.Aprobar, request.Comentario);
            return NoContent();
        }

        [HttpPost("{id:int}/finalizar")]
        public async Task<IActionResult> FinalizarCampania(int id, FinalizarCampaniaRequest request)
        {
            await _campanias.FinalizarAsync(id, request.Confirmar, Actor);
            return NoContent();
        }

        [HttpPost("{id:int}/ejecutar")]
        public async Task<IActionResult> EjecutarCampania(int id)
        {
            var (campania, usuarios, omitidos) = await _campanias.PrepararEjecucionAsync(id, Actor);

            // los enlaces usan la URL publica configurada, no la cabecera Host manipulable
            var baseUrl = _configuration["Api:PublicBaseUrl"]?.TrimEnd('/') is { Length: > 0 } configurada
                ? configurada
                : $"{Request.Scheme}://{Request.Host}";
            var envios = await _db.EnviosSimulacion
                .Where(e => e.Id_Campania == id && e.EstadoEntrega == "pending")
                .OrderBy(e => e.Id_Envio)
                .ToListAsync();
            var aceptados = 0;
            var fallidos = 0;

            foreach (var envio in envios)
            {
                // se guarda el intento aunque el proceso termine antes de la respuesta del proveedor
                envio.FechaEnvio = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // serializa cada envio con el cierre de la campania
                await using var tx = await _db.Database.BeginTransactionAsync();
                var now = DateTime.UtcNow;
                var active = await _db.CampaniasSimulacion
                    .Where(c => c.Id_Campania == id && c.Id_Estado == 2 &&
                        (c.FechaFinProgramada == null || c.FechaFinProgramada >= now))
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Id_Estado, 2));
                if (active == 0)
                {
                    // la campania se cerro durante el envio, el correo no sale y no conserva fecha
                    envio.FechaEnvio = null;
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                    break;
                }

                var usuario = usuarios.Single(u => u.Id == envio.Id_Usuario);
                var linkTrampa = $"{baseUrl}/api/campanias-phishing/click/{envio.TokenUuid}";
                var linkPixel = $"{baseUrl}/api/campanias-phishing/track/{envio.TokenUuid}";
                var frontend = (_configuration["Frontend:BaseUrl"] ?? "http://localhost:5266").TrimEnd('/');
                var linkReporte = $"{frontend}/dashboard-usuario.html?reportar={envio.TokenUuid}";
                var cuerpo = campania.CuerpoHtmlRegistro!
                    .Replace("{{NombreUsuario}}", System.Net.WebUtility.HtmlEncode(usuario.Nombre))
                    .Replace("{{LinkTrampa}}", linkTrampa)
                    .Replace("{{LinkReporte}}", System.Net.WebUtility.HtmlEncode(linkReporte))
                    + $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(linkReporte)}\">Reportar mensaje sospechoso</a></p><img src=\"{linkPixel}\" width=\"1\" height=\"1\" style=\"display:none;\" />";

                try
                {
                    await _emailSender.EnviarAsync(
                        usuario.Email!,
                        $"{usuario.Nombre} {usuario.Apellido}",
                        campania.AsuntoRegistro!,
                        cuerpo);
                    envio.EstadoEntrega = "accepted";
                    aceptados++;
                }
                catch
                {
                    envio.EstadoEntrega = "failed";
                    fallidos++;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            return Ok(new {
                mensaje = $"Campaña procesada: {aceptados} correos aceptados por el proveedor y {fallidos} fallidos. La aceptación no confirma la entrega."
                    + (omitidos > 0 ? $" Se omitió a {omitidos} persona(s) que alcanzaron el límite de simulaciones de 30 días." : ""),
                victimasSeleccionadas = aceptados,
                enviosFallidos = fallidos,
                colaboradoresSinAsignacion = 0,
                omitidosPorFrecuencia = omitidos
            });
        }

        [HttpGet("track/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> RastrearApertura(Guid token)
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            var now = DateTime.UtcNow;
            await _db.EnviosSimulacion
                .Where(e => e.TokenUuid == token
                    && !e.FueAbierto
                    && e.EstadoEntrega != "failed"
                    && e.Campania.Id_Estado == 2
                    && (e.Campania.FechaFinProgramada == null || e.Campania.FechaFinProgramada >= now))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.FueAbierto, true).SetProperty(e => e.FechaApertura, now));

            byte[] pixelInvisible = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            await RegistrarHttpAsync(token, "apertura", "/api/campanias-phishing/track/{token}", 200, pixelInvisible.Length, reloj);

            return File(pixelInvisible, "image/gif");
        }

        [HttpGet("click/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> RastrearClick(Guid token)
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            await _resultados.RegistrarClickAsync(token);

            var frontBaseUrl = _configuration["Frontend:BaseUrl"] ?? "https://localhost:44320";

            // con pagina senuelo el clic muestra primero un formulario simulado y luego la capacitacion
            var pagina = await _resultados.UsaSenueloAsync(token) ? "senuelo.html" : "phishing-educacion.html";
            await RegistrarHttpAsync(token, "clic", "/api/campanias-phishing/click/{token}", 302, 0, reloj);

            return Redirect($"{frontBaseUrl}/{pagina}?token={token}");
        }

        // registra el envio del formulario senuelo sin cuerpo, misma respuesta para cualquier token
        [HttpPost("senuelo/{token:guid}")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(proyecto.api.Configuration.SecurityHardening.PoliticaSesion)]
        public async Task<IActionResult> RegistrarSenuelo(Guid token, CancellationToken ct)
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            await _resultados.RegistrarCredencialesAsync(token, ct);
            await RegistrarHttpAsync(token, "credenciales", "/api/campanias-phishing/senuelo/{token}", 204, 0, reloj);

            return NoContent();
        }

        // guarda la peticion seudonimizada para HDBSCAN, un fallo nunca afecta al colaborador
        private async Task RegistrarHttpAsync(
            Guid token,
            string tipo,
            string ruta,
            int status,
            long bytes,
            System.Diagnostics.Stopwatch reloj)
        {
            try
            {
                await _simulacionHttp.RegistrarAsync(token, tipo,
                    SeudonimosHttp.Meta(HttpContext, _configuration, ruta, token, status, bytes, reloj.Elapsed.TotalMilliseconds));
            }
            catch (Exception) { /* telemetria opcional */ }
        }

        [HttpGet("criticidades")]
        public async Task<IActionResult> ObtenerCriticidades()
        {
            var criticidades = await _db.NivelesCriticidad
                .AsNoTracking()
                .Select(c => new {
                    idCriticidad = c.Id_NivelCriticidad,
                    nivel = c.Nivel
                })
                .ToListAsync();

            return Ok(criticidades);
        }
    }
}
