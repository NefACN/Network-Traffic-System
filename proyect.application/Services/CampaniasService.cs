using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class CampaniasService(IApplicationDbContext db, IAuditoriaService auditoria, GobernanzaService gobernanza)
{
    public const string VersionMedicion = "preventivo-v3-nist";
    public static readonly IReadOnlyList<string> VersionesComparables = ["preventivo-v2-reportes", VersionMedicion];

    public static List<int> Participantes(CampaniaSimulacion c) =>
        JsonSerializer.Deserialize<List<int>>(c.ParticipantesSeleccionadosJson) ?? [];

    public IQueryable<ApplicationUser> Candidatos(List<int> areas, int? sucursal)
    {
        var now = DateTime.UtcNow;

        return db.Users.AsNoTracking()
            .Where(u => u.Activo && u.Id_Area != null && areas.Contains(u.Id_Area.Value)
                && db.AreasOperativas.Any(a => a.Id_Area == u.Id_Area && a.Activo)
                && u.Id_Sucursal != null
                && (!sucursal.HasValue || u.Id_Sucursal == sucursal)
                && db.Sucursales.Any(s => s.Id_Sucursal == u.Id_Sucursal && s.Activo)
                && u.Email != null
                && u.Email.Trim() != ""

                // excluye al personal en la lista de exclusion
                && !db.Set<ExclusionSimulacion>().Any(x => x.IdUsuario == u.Id
                    && x.Activa && x.Desde <= now
                    && (x.Hasta == null || x.Hasta > now))
                && !db.RolUsuario.Any(r => r.IdUsuario == u.Id
                    && r.Activo && r.Rol.Activo && r.FechaInicio <= now
                    && (r.FechaFin == null || r.FechaFin > now)
                    && (r.Rol.Nombre == RolesSistemas.RolAdmin || r.Rol.Nombre == RolesSistemas.RolGerente)));
    }

    public async Task<object> VistaParticipantesAsync(ParticipantesCampaniaRequest request)
    {
        await ValidarUnidadesAsync(request.AreasSeleccionadas, request.IdSucursalObjetivo);

        var participantes = await Candidatos(request.AreasSeleccionadas, request.IdSucursalObjetivo)
            .OrderBy(u => u.Nombre)
            .ThenBy(u => u.Apellido)
            .ThenBy(u => u.Id)
            .Select(u => new {
                idUsuario = u.Id,
                nombre = u.Nombre + " " + u.Apellido,
                correo = u.Email,
                sucursal = u.Sucursal!.Nombre,
                area = u.AreaOperativa!.Nombre
            }).ToListAsync();

        var politica = await gobernanza.PoliticaAsync();
        var saturados = await gobernanza.SaturadosAsync(participantes.Select(x => x.idUsuario), 0, politica, default);
        var now = DateTime.UtcNow;
        var excluidos = await db.Users.CountAsync(
            u => u.Activo && u.Id_Area != null
            && request.AreasSeleccionadas.Contains(u.Id_Area.Value)
            && (!request.IdSucursalObjetivo.HasValue || u.Id_Sucursal == request.IdSucursalObjetivo)
            && db.Set<ExclusionSimulacion>().Any(x => x.IdUsuario == u.Id
                && x.Activa
                && x.Desde <= now
                && (x.Hasta == null || x.Hasta > now)));

        return new {
            total = participantes.Count,
            participantes = participantes.Select(
                x => new {
                    x.idUsuario,
                    x.nombre,
                    x.correo,
                    x.sucursal,
                    x.area,
                    limiteFrecuencia = saturados.Contains(x.idUsuario)
                }),
            excluidosPorLista = excluidos,
            alcanzaronLimite = saturados.Count,
            limiteFrecuencia = politica.MaxSimulacionesPorPersona
        };
    }

    public async Task<int> GuardarAsync(int? id, CrearCampaniaRequest request, int actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        CampaniaSimulacion c;

        if (id.HasValue)
        {
            await BloquearPendienteAsync(id.Value);
            c = await db.CampaniasSimulacion
                .Include(x => x.Segmentaciones)
                .SingleAsync(x => x.Id_Campania == id);
        }
        else
        {
            c = new CampaniaSimulacion {
                Id_Estado = 1,
                CreadoPor = actor
            };
            db.CampaniasSimulacion.Add(c);
        }

        await ValidarAsync(request);

        c.Titulo = request.Titulo.Trim();
        c.Objetivo = request.Objetivo.Trim();
        c.TipoEvaluacion = request.TipoEvaluacion;
        c.NivelDificultad = request.NivelDificultad;
        c.Id_Plantilla = request.IdPlantilla;
        c.IdSucursalObjetivo = request.IdSucursalObjetivo;
        c.FechaInicioProgramada = request.FechaInicioProgramada;
        c.FechaFinProgramada = request.FechaFinProgramada;
        c.ParticipantesSeleccionadosJson = JsonSerializer.Serialize(
            (request.ParticipantesSeleccionados ?? [])
            .Distinct()
            .Order()
            .ToList());

        c.EstadoAprobacion = "borrador";
        c.SolicitadaPor = null;
        c.FechaSolicitud = null;
        c.RevisadaPor = null;
        c.FechaRevision = null;
        c.ComentarioRevision = null;

        var areas = request.AreasSeleccionadas.Distinct().ToList();

        db.SegmentacionesCampaniaArea.RemoveRange(c.Segmentaciones.Where(s => !areas.Contains(s.Id_Area)));

        foreach (var area in areas.Where(a => !c.Segmentaciones.Any(s => s.Id_Area == a)))
            c.Segmentaciones.Add(new SegmentacionCampaniArea { Id_Area = area });

        await db.SaveChangesAsync();

        await auditoria.RegistrarAsync(id.HasValue ? "EditarCampania" : "CrearCampania", true, actor, entidad: "Campania",
            idEntidadAfectada: c.Id_Campania.ToString(), detalles: new { c.Titulo, c.TipoEvaluacion });

        await tx.CommitAsync();
        return c.Id_Campania;
    }

    private async Task VerificarPretextosAsync(PlantillaPhishing plantilla, PoliticaGobernanza politica)
    {
        var hallados = GobernanzaService.PretextosDetectados(
            plantilla.AsuntoCorreo + " " + plantilla.CuerpoHtml,
            GobernanzaService.Terminos(politica));
        if (hallados.Count > 0)
            throw new BusinessRuleException($"La plantilla usa pretextos prohibidos por la política ética ({string.Join(", ", hallados)}). Cambia el escenario para no explotar temas sensibles.");

        await Task.CompletedTask;
    }

    public async Task SolicitarAprobacionAsync(int id, int actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await BloquearPendienteAsync(id);

        var c = await db.CampaniasSimulacion
            .Include(x => x.Segmentaciones)
            .Include(x => x.Plantilla)
            .SingleAsync(x => x.Id_Campania == id);

        if (c.EstadoAprobacion is "pendiente" or "aprobada")
            throw new BusinessRuleException("La campaña ya tiene una solicitud de aprobación vigente.", BusinessError.Conflict);

        await ValidarAsync(new(
            c.Titulo,
            c.Id_Plantilla,
            c.FechaInicioProgramada,
            c.FechaFinProgramada,
            c.Segmentaciones.Select(s => s.Id_Area).ToList(),
            c.IdSucursalObjetivo,
            c.Objetivo,
            c.TipoEvaluacion,
            Participantes(c),
            c.NivelDificultad));

        await VerificarPretextosAsync(c.Plantilla, await gobernanza.PoliticaAsync());

        c.EstadoAprobacion = "pendiente";
        c.SolicitadaPor = actor;
        c.FechaSolicitud = DateTime.UtcNow;

        c.RevisadaPor = null;
        c.FechaRevision = null;
        c.ComentarioRevision = null;

        await db.SaveChangesAsync();

        await auditoria.RegistrarAsync("SolicitarAprobacionCampania", true, actor, entidad: "Campania", idEntidadAfectada: id.ToString());

        await tx.CommitAsync();
    }

    public async Task RevisarAsync(int id, int actor, bool aprobar, string? comentario)
    {
        await using var tx = await db.Database.BeginTransactionAsync();

        await BloquearPendienteAsync(id);

        var c = await db.CampaniasSimulacion.Include(x => x.Plantilla).SingleAsync(x => x.Id_Campania == id);

        if (c.EstadoAprobacion != "pendiente")
            throw new BusinessRuleException("La campaña no tiene una solicitud de aprobación pendiente.", BusinessError.Conflict);

        if (actor == c.CreadoPor || actor == c.SolicitadaPor)
            throw new BusinessRuleException("Debe revisarla un administrador distinto de quien la creó o solicitó la aprobación.", BusinessError.Forbidden);

        if (!aprobar && string.IsNullOrWhiteSpace(comentario))
            throw new BusinessRuleException("Indica el motivo del rechazo para que pueda corregirse.");

        if (aprobar)
            await VerificarPretextosAsync(c.Plantilla, await gobernanza.PoliticaAsync());

        c.EstadoAprobacion = aprobar ? "aprobada" : "rechazada";
        c.RevisadaPor = actor;
        c.FechaRevision = DateTime.UtcNow;

        c.ComentarioRevision = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();

        await db.SaveChangesAsync();

        await auditoria.RegistrarAsync(aprobar ? "AprobarCampania" : "RechazarCampania", true, actor, entidad: "Campania",
            idEntidadAfectada: id.ToString(), detalles: new { comentario = c.ComentarioRevision });
        await tx.CommitAsync();
    }

    public async Task<(CampaniaSimulacion Campania, List<ApplicationUser> Usuarios, int OmitidosFrecuencia)> PrepararEjecucionAsync(
        int id,
        int actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await BloquearPendienteAsync(id);

        var c = await db.CampaniasSimulacion
            .Include(x => x.Segmentaciones)
            .Include(x => x.Plantilla).ThenInclude(p => p.Senales)
            .Include(x => x.Plantilla).ThenInclude(p => p.Alineaciones)
            .AsSplitQuery()
            .SingleAsync(x => x.Id_Campania == id);

        var politica = await gobernanza.PoliticaAsync();

        if (politica.RequiereAprobacion && c.EstadoAprobacion != "aprobada")
            throw new BusinessRuleException("La campaña requiere la aprobación de un segundo administrador antes de ejecutarse.");

        if (GobernanzaService.FueraDeVentana(politica, DateTime.UtcNow) is { } fuera)
            throw new BusinessRuleException(fuera);

        await VerificarPretextosAsync(c.Plantilla, politica);

        var areas = c.Segmentaciones.Select(s => s.Id_Area).ToList();

        var seleccion = Participantes(c);

        await ValidarAsync(new(c.Titulo, c.Id_Plantilla, c.FechaInicioProgramada, c.FechaFinProgramada, areas,
            c.IdSucursalObjetivo, c.Objetivo, c.TipoEvaluacion, seleccion, c.NivelDificultad));

        if (c.FechaInicioProgramada > DateTime.UtcNow)
            throw new BusinessRuleException("La fecha de inicio programada todavía no ha llegado.");

        var query = Candidatos(areas, c.IdSucursalObjetivo);

        if (seleccion.Count > 0)
            query = query.Where(u => seleccion.Contains(u.Id));

        var usuarios = await query.OrderBy(u => u.Id).ToListAsync();
        // omite a quienes ya recibieron el maximo de simulaciones en 30 dias (evita fatiga)
        var saturados = await gobernanza.SaturadosAsync(usuarios.Select(u => u.Id), id, politica, default);
        usuarios = usuarios.Where(u => !saturados.Contains(u.Id)).ToList();

        if (usuarios.Count == 0)
            throw new BusinessRuleException(saturados.Count > 0
                ? "Todos los participantes alcanzaron el límite de simulaciones de los últimos 30 días."
                : "No hay participantes válidos en el alcance seleccionado.");

        var puntos = await db.Set<ReglasGamificacion>().Select(r => r.PuntosRefuerzo).SingleAsync();
        c.Id_Estado = 2;
        c.AsuntoRegistro = c.Plantilla.AsuntoCorreo;
        c.CuerpoHtmlRegistro = c.Plantilla.CuerpoHtml;
        c.VersionMedicionRegistro = VersionMedicion;
        c.NivelDificultadRegistro = c.NivelDificultad;
        c.IdCategoriaRegistro = c.Plantilla.Id_CategoriaPegunta;

        // se copia la alineacion por area para que editar la plantilla no altere resultados
        var senales = c.Plantilla.Senales.OrderBy(s => s.Orden).ThenBy(s => s.Id).ToList();
        var especificas = c.Plantilla.Alineaciones.ToDictionary(a => a.IdArea, a => a.Nivel);
        string Alineacion(int? area) =>
            area is { } a && especificas.TryGetValue(a, out var nivel) ? nivel : c.Plantilla.AlineacionGeneral;
        var areasUsuarios = usuarios.Select(u => u.Id_Area).Distinct().ToList();
        var nombresAreas = await db.AreasOperativas
            .Where(a => areasUsuarios.Contains(a.Id_Area))
            .ToDictionaryAsync(a => a.Id_Area, a => a.Nombre);
        c.PaginaSenueloRegistro = c.Plantilla.PaginaSenuelo;

        // datos de ensenanza congelados: editar la plantilla no cambia lo que se explica
        c.EnsenanzaRegistro = JsonSerializer.Serialize(new {
            remitenteNombre = c.Plantilla.RemitenteNombre,
            remitenteCorreo = c.Plantilla.RemitenteCorreo,
            enlaceMostrado = c.Plantilla.EnlaceMostrado
        });

        c.PhishScaleRegistro = JsonSerializer.Serialize(new {
            version = PhishScale.Version,
            cantidadSenales = senales.Count,
            categoriaSenales = PhishScale.CategoriaSenales(senales.Count),
            alineacionGeneral = c.Plantilla.AlineacionGeneral,
            senales = senales.Select(s => new { tipo = s.Tipo, descripcion = s.Descripcion, fragmento = s.Fragmento }),
            porArea = areasUsuarios.Where(a => a.HasValue).OrderBy(a => a).Select(a => new {
                idArea = a,
                area = nombresAreas.GetValueOrDefault(a!.Value),
                alineacion = Alineacion(a),
                dificultad = PhishScale.Dificultad(senales.Count, Alineacion(a)),
            }),
        });

        foreach (var u in usuarios)
            db.EnviosSimulacion.Add(new EnvioSimulacion {
                Id_Campania = id,
                Id_Usuario = u.Id,
                TokenUuid = Guid.NewGuid(),
                ContextoRegistrado = true,
                IdSucursalRegistro = u.Id_Sucursal,
                IdAreaRegistro = u.Id_Area,
                IdCategoriaRegistro = c.Plantilla.Id_CategoriaPegunta,
                EstadoEntrega = "pending",
                PuntosRefuerzoConfigurados = puntos,
                AlineacionRegistro = Alineacion(u.Id_Area),
                DificultadNistRegistro = PhishScale.Dificultad(senales.Count, Alineacion(u.Id_Area))
            });

        await db.SaveChangesAsync();

        await auditoria.RegistrarAsync("IniciarCampania", true, actor, entidad: "Campania", idEntidadAfectada: id.ToString(),
            detalles: new {
                participantes = usuarios.Count,
                omitidosPorFrecuencia = saturados.Count,
                c.EstadoAprobacion,
                revisadaPor = c.RevisadaPor
            });

        await tx.CommitAsync();
        return (c, usuarios, saturados.Count);
    }

    public async Task FinalizarAsync(int id, bool confirmar, int actor)
    {
        if (!confirmar)
            throw new BusinessRuleException("Confirma la finalización de la campaña.");

        await using var tx = await db.Database.BeginTransactionAsync();
        // mismo bloqueo que el envio: al cerrar no se inician mas correos
        var locked = await db.CampaniasSimulacion.Where(c => c.Id_Campania == id && c.Id_Estado == 2)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Id_Estado, 2));

        if (locked == 0)
        {
            var existing = await db.CampaniasSimulacion.AsNoTracking().SingleOrDefaultAsync(c => c.Id_Campania == id)
                ?? throw new BusinessRuleException("La campaña no existe.", BusinessError.NotFound);
            if (existing.FechaFinalizacion.HasValue)
                return;

            throw new BusinessRuleException("Solo puedes finalizar una campaña en ejecución.", BusinessError.Conflict);
        }

        var estado = await db.EstadosFlujo
            .Where(e => e.Categoria == "Campania" && e.NombreEstado == "Finalizada")
            .Select(e => e.Id_Estado)
            .FirstAsync();

        var now = DateTime.UtcNow;

        await db.CampaniasSimulacion.Where(c => c.Id_Campania == id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Id_Estado, estado)
            .SetProperty(c => c.FechaFinalizacion, now)
            .SetProperty(c => c.FinalizacionAnticipada, c => !c.FechaFinProgramada.HasValue || c.FechaFinProgramada > now));

        await db.EnviosSimulacion.Where(e => e.Id_Campania == id && e.EstadoEntrega == "pending")
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.EstadoEntrega, "canceled"));

        await auditoria.RegistrarAsync("FinalizarCampania", true, actor, entidad: "Campania", idEntidadAfectada: id.ToString());

        await tx.CommitAsync();
    }

    private async Task BloquearPendienteAsync(int id)
    {
        var locked = await db.CampaniasSimulacion.Where(c => c.Id_Campania == id && c.Id_Estado == 1)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Id_Estado, 1));

        if (locked > 0)
            return;

        if (!await db.CampaniasSimulacion.AnyAsync(c => c.Id_Campania == id))
            throw new BusinessRuleException("La campaña no existe.", BusinessError.NotFound);

        throw new BusinessRuleException("La campaña ya fue ejecutada o finalizada; no se puede editar ni volver a iniciar.");
    }

    private async Task ValidarUnidadesAsync(List<int> areas, int? sucursal)
    {
        if (areas is null || areas.Count == 0 || areas.Count > 5000 || areas.Any(a => a < 1) ||
            await db.AreasOperativas.CountAsync(a => a.Activo && areas.Contains(a.Id_Area)) != areas.Distinct().Count())
            throw new BusinessRuleException("Selecciona al menos un área activa válida.");

        if (sucursal.HasValue && !await db.Sucursales.AnyAsync(s => s.Id_Sucursal == sucursal && s.Activo))
            throw new BusinessRuleException("La sucursal no existe o está inactiva.");
    }

    private async Task ValidarAsync(CrearCampaniaRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Titulo)
            || string.IsNullOrWhiteSpace(r.Objetivo)
            || r.Titulo.Length > 100
            || r.Objetivo.Length > 1000)
            throw new BusinessRuleException("Completa el título y el objetivo de la campaña.");

        if (r.TipoEvaluacion is not ("inicial" or "posterior" or "seguimiento"))
            throw new BusinessRuleException("Selecciona evaluación inicial, posterior o seguimiento.");

        if (r.NivelDificultad is < 1 or > 5)
            throw new BusinessRuleException("La dificultad debe estar entre 1 y 5.");

        if (!r.FechaInicioProgramada.HasValue || r.FechaInicioProgramada.Value.Kind != DateTimeKind.Utc ||
            (r.FechaFinProgramada.HasValue && r.FechaFinProgramada.Value.Kind != DateTimeKind.Utc) ||
            r.FechaInicioProgramada >= r.FechaFinProgramada || r.FechaFinProgramada <= DateTime.UtcNow)
            throw new BusinessRuleException("Indica un inicio válido y un fin posterior al inicio y a la fecha actual. Envía las fechas con zona horaria UTC.");

        if (!await db.PlantillasPhishing.AnyAsync(p => p.Id_Plantilla == r.IdPlantilla && p.Habilitada))
            throw new BusinessRuleException("La plantilla no existe o está deshabilitada.");

        await ValidarUnidadesAsync(r.AreasSeleccionadas, r.IdSucursalObjetivo);
        var seleccion = (r.ParticipantesSeleccionados ?? []).Distinct().ToList();

        if (seleccion.Count > 5000)
            throw new BusinessRuleException("Selecciona como máximo 5000 participantes individuales.");

        var candidatos = Candidatos(r.AreasSeleccionadas, r.IdSucursalObjetivo);

        if (seleccion.Count > 0)
        {
            if (await candidatos.CountAsync(u => seleccion.Contains(u.Id)) != seleccion.Count)
                throw new BusinessRuleException("Uno de los participantes seleccionados ya no cumple la segmentación, no está activo o está en la lista de exclusión. Actualiza la lista.");
        }
        else if (!await candidatos.AnyAsync())
            throw new BusinessRuleException("No hay colaboradores válidos en el alcance seleccionado; no se puede guardar la campaña.");
    }
}
