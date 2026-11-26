using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class ActividadesEducativasService(IApplicationDbContext db, GamificacionService juego, MicroleccionesService microlecciones)
{
    public const int MaxSeccionesMicro = 3, MaxCaracteresMicro = 2000, MaxPreguntasMicro = 3;

    public async Task<object> GuardarAsync(int? id, ActividadEducativaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Titulo) || string.IsNullOrWhiteSpace(request.Descripcion))
            throw new BusinessRuleException("Completa título y descripción.");

        if (!await db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == request.IdCategoria, ct))
            throw new BusinessRuleException("Selecciona un tema existente.");

        if (request.Puntos is < 0 or > 500 || request.DiasRepeticion is < 1 or > 365 || request.PorcentajeAprobacion is < 1 or > 100)
            throw new BusinessRuleException("Revisa las reglas de puntos, repetición y aprobación.");

        var secciones = request.Secciones ?? [];

        if (secciones.Count > 20 || secciones.Any(s => string.IsNullOrWhiteSpace(s.Titulo) ||
            string.IsNullOrWhiteSpace(s.Contenido) || s.Titulo.Length > 120 || s.Contenido.Length > 12000))
            throw new BusinessRuleException("Cada sección requiere título y material educativo; máximo 20 secciones.");

        var ids = (request.IdsPreguntas ?? []).Distinct().ToList();

        if (ids.Count > 30)
            throw new BusinessRuleException("El cuestionario admite hasta 30 preguntas.");

        var preguntas = await db.PregutasConcientizacion.Include(p => p.Opciones)
            .Where(p => ids.Contains(p.Id_Pregunta))
            .OrderBy(p => p.Id_Pregunta)
            .ToListAsync(ct);

        if (preguntas.Count != ids.Count || preguntas.Any(p => p.Id_Categoria != request.IdCategoria))
            throw new BusinessRuleException("Todas las preguntas deben existir y pertenecer al tema seleccionado.");

        if (request.ParaCredenciales && !request.EsMicroleccion)
            throw new BusinessRuleException("Solo una microlección puede marcarse para credenciales.");

        if (request.EsMicroleccion && (secciones.Count > MaxSeccionesMicro ||
            secciones.Sum(s => (s.Contenido ?? "").Length) > MaxCaracteresMicro || ids.Count > MaxPreguntasMicro))
            throw new BusinessRuleException(
                $"Una microlección admite hasta {MaxSeccionesMicro} secciones, {MaxCaracteresMicro} caracteres de material y {MaxPreguntasMicro} preguntas.");

        if (request.Habilitada && (secciones.Count == 0 || preguntas.Count == 0 ||
            preguntas.Any(p => p.Opciones.Count < 2 || p.Opciones.Count(o => o.EsCorrecta) != 1 ||
            p.Opciones.Any(o => string.IsNullOrWhiteSpace(o.TextoOpcion)))))
            throw new BusinessRuleException("Para habilitar, incorpora material y al menos una pregunta válida. Cada pregunta requiere dos opciones y exactamente una respuesta correcta.");

        var actividad = id.HasValue ? await db.Set<ActividadEducativa>().Include(a => a.Preguntas).SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new BusinessRuleException("Actividad no encontrada.", BusinessError.NotFound) : new ActividadEducativa();

        if (!id.HasValue)
            db.Set<ActividadEducativa>().Add(actividad);
        else
            actividad.Revision++;

        actividad.Titulo = request.Titulo.Trim();
        actividad.Descripcion = request.Descripcion.Trim();
        actividad.IdCategoria = request.IdCategoria;
        actividad.Secciones = JsonSerializer.Serialize(secciones);
        actividad.Habilitada = request.Habilitada;
        actividad.Puntos = request.Puntos;
        actividad.DiasRepeticion = request.DiasRepeticion;
        actividad.PorcentajeAprobacion = request.PorcentajeAprobacion;
        actividad.PermitirRepaso = request.PermitirRepaso;
        actividad.ActualizadaEn = DateTime.UtcNow;
        actividad.EsMicroleccion = request.EsMicroleccion;
        actividad.ParaCredenciales = request.ParaCredenciales;
        actividad.PreguntasPublicadas = JsonSerializer.Serialize(preguntas.Select(p => new PreguntaPublicada(p.Id_Pregunta, p.TextoPregunta, p.ExplicacionEducativa,
            p.Opciones.Select(o => new OpcionPublicada(o.Id_Opcion, o.TextoOpcion, o.EsCorrecta)).ToList())));
        db.Set<ActividadPregunta>().RemoveRange(actividad.Preguntas.Where(p => !ids.Contains(p.IdPregunta)));

        foreach (var pregunta in ids.Where(p => !actividad.Preguntas.Any(a => a.IdPregunta == p)))
            actividad.Preguntas.Add(new ActividadPregunta { IdPregunta = pregunta });

        await db.SaveChangesAsync(ct);

        return new { actividad.Id, actividad.Revision };
    }
    public async Task<object> DisponiblesAsync(int usuario, CancellationToken ct)
    {
        var actividades = await db.Set<ActividadEducativa>()
            .AsNoTracking()
            .Where(a => a.Habilitada)
            .OrderBy(a => a.Titulo)
            .Select(a => new {
                a.Id,
                a.Titulo,
                a.Descripcion,
                tema = a.Categoria.Nombre,
                a.IdCategoria,
                a.Puntos,
                a.DiasRepeticion,
                a.PorcentajeAprobacion,
                a.PermitirRepaso,
                ultimoExito = db.IntentoCursoVoluntario
                .Where(i => i.Id_Usuario == usuario && i.IdActividad == a.Id && i.Exitoso == true)
                .Max(i => i.FechaCompletado),
                idIntento = db.IntentoCursoVoluntario
                .Where(i => i.Id_Usuario == usuario && i.IdActividad == a.Id && i.FechaCompletado == null)
                .Select(i => (int?)i.Id_Intento).FirstOrDefault(),
                a.EsMicroleccion,
                motivoAsignacion = db.Set<AsignacionMicroleccion>()
                .Where(m => m.IdUsuario == usuario && m.IdActividad == a.Id && m.FechaCompletado == null)
                    .OrderBy(m => m.FechaAsignacion).Select(m => m.Motivo).FirstOrDefault()
            }).ToListAsync(ct);

        // primero las lecciones asignadas tras una simulacion
        return actividades.OrderByDescending(a => a.motivoAsignacion != null)
            .Select(a => new {
                a.Id,
                a.Titulo,
                a.Descripcion,
                a.tema,
                a.IdCategoria,
                a.Puntos,
                a.DiasRepeticion,
                a.PorcentajeAprobacion,
                a.EsMicroleccion,
                asignada = a.motivoAsignacion != null,
                a.motivoAsignacion,
                a.ultimoExito,
                a.idIntento,
                otorgaPuntos = a.ultimoExito == null || a.ultimoExito <= DateTime.UtcNow.AddDays(-a.DiasRepeticion),
                puedeIniciar = a.idIntento.HasValue || a.PermitirRepaso || a.ultimoExito == null || a.ultimoExito <= DateTime.UtcNow.AddDays(-a.DiasRepeticion),
                proximaRecompensa = a.ultimoExito?.AddDays(a.DiasRepeticion)
            });
    }
    public async Task<object> IniciarAsync(int actividadId, int usuario, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await juego.BloquearUsuarioAsync(usuario, ct);

        var abierto = await db.IntentoCursoVoluntario.FirstOrDefaultAsync(i => i.Id_Usuario == usuario && i.IdActividad == actividadId && i.FechaCompletado == null, ct);

        if (abierto is not null)
            return Vista(abierto);

        var actividad = await db.Set<ActividadEducativa>()
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == actividadId && a.Habilitada, ct)
            ?? throw new BusinessRuleException("La actividad no está habilitada.", BusinessError.NotFound);

        var ultimo = await db.IntentoCursoVoluntario
            .Where(i => i.Id_Usuario == usuario && i.IdActividad == actividadId && i.Exitoso == true)
            .MaxAsync(i => i.FechaCompletado, ct);

        if (!actividad.PermitirRepaso && ultimo > DateTime.UtcNow.AddDays(-actividad.DiasRepeticion))
            throw new BusinessRuleException("Esta actividad estará disponible nuevamente cuando termine su periodo de repetición.");

        var persona = await db.Users
            .Where(u => u.Id == usuario)
            .Select(u => new { u.Id_Sucursal, u.Id_Area })
            .SingleAsync(ct);

        var preguntas = JsonSerializer.Deserialize<List<PreguntaPublicada>>(actividad.PreguntasPublicadas)!;

        if (preguntas.Count == 0)
            throw new BusinessRuleException("La actividad no tiene cuestionario publicado.");

        var intento = new IntentoCursoVoluntario {
            Id_Usuario = usuario,
            Id_Categoria = actividad.IdCategoria,
            IdActividad = actividad.Id,
            RevisionActividad = actividad.Revision,
            FechaInicio = DateTime.UtcNow,
            ContextoRegistrado = true,
            IdSucursalRegistro = persona.Id_Sucursal,
            IdAreaRegistro = persona.Id_Area,
            TituloRegistro = actividad.Titulo,
            ContenidoRegistro = actividad.Secciones,
            CuestionarioRegistro = actividad.PreguntasPublicadas,
            PuntosConfigurados = actividad.Puntos,
            DiasRepeticionConfigurados = actividad.DiasRepeticion,
            AprobacionConfigurada = actividad.PorcentajeAprobacion,
            TotalPreguntas = preguntas.Count
        };

        db.IntentoCursoVoluntario.Add(intento);

        foreach (var pregunta in preguntas)
            intento.PreguntasAsignadas.Add(new PreguntaAsignadaCurso { Id_Pregunta = pregunta.IdPregunta });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Vista(intento);
    }

    private async Task<IntentoCursoVoluntario> Buscar(
        int id, int usuario, CancellationToken ct) =>
        await db.IntentoCursoVoluntario.SingleOrDefaultAsync(i => i.Id_Intento == id && i.Id_Usuario == usuario && i.IdActividad != null, ct)
        ?? throw new BusinessRuleException("Intento no encontrado.", BusinessError.NotFound);

    private static object Vista(IntentoCursoVoluntario i) => new {
        idIntento = i.Id_Intento,
        titulo = i.TituloRegistro,
        i.RevisionActividad,
        secciones = JsonSerializer.Deserialize<List<SeccionEducativa>>(i.ContenidoRegistro!),
        evaluado = i.FechaEvaluacion != null,
        finalizado = i.FechaCompletado != null,
        i.ContenidoRevisado,
        puntosPosibles = i.PuntosConfigurados,
        aprobacion = i.AprobacionConfigurada
    };

    public async Task<object> IntentoAsync(int id, int usuario, CancellationToken ct) => Vista(await Buscar(id, usuario, ct));
    public async Task<object> CuestionarioAsync(int id, int usuario, CancellationToken ct)
    {
        var intento = await Buscar(id, usuario, ct);

        intento.ContenidoRevisado = true;

        await db.SaveChangesAsync(ct);

        return new {
            preguntas = JsonSerializer.Deserialize<List<PreguntaPublicada>>(intento.CuestionarioRegistro!)!
            .Select(p => new {
                p.IdPregunta,
                p.TextoPregunta,
                opciones = p.Opciones
                .Select(
                    o => new {
                        o.IdOpcion,
                        o.TextoOpcion
                    })
            })
        };
    }

    private static object Resultado(IntentoCursoVoluntario i) => new {
        i.Porcentaje,
        i.Aciertos,
        i.TotalPreguntas,
        aprobado = i.Porcentaje >= i.AprobacionConfigurada,
        i.AprobacionConfigurada,
        finalizado = i.FechaCompletado != null,
        i.PuntosObtenidos,
        respuestas = JsonSerializer.Deserialize<List<RespuestaEvaluada>>(i.RespuestasRegistro ?? "[]")
    };

    public async Task<object> ResultadoAsync(int id, int usuario, CancellationToken ct)
    {
        var intento = await Buscar(id, usuario, ct);

        if (intento.FechaEvaluacion == null)
            throw new BusinessRuleException("El cuestionario todavia no fue evaluado");

        return Resultado(intento);
    }
    public async Task<object> EvaluarAsync(int id, int usuario, ValidarRespuestasRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.IntentoCursoVoluntario
            .Where(i => i.Id_Intento == id && i.Id_Usuario == usuario)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PuntosObtenidos, i => i.PuntosObtenidos), ct);

        var intento = await Buscar(id, usuario, ct);

        if (intento.FechaEvaluacion != null)
            return Resultado(intento);

        if (!intento.ContenidoRevisado)
            throw new BusinessRuleException("Revisa el material y accede al cuestionario antes de responder.");

        var preguntas = JsonSerializer.Deserialize<List<PreguntaPublicada>>(intento.CuestionarioRegistro!)!;

        var seleccionadas = request.IdsOpcionesSeleccionadas ?? [];

        if (seleccionadas.Count != preguntas.Count ||
            seleccionadas.Distinct().Count() != seleccionadas.Count ||
            preguntas.Any(
                p => p.Opciones.Count(
                    o => seleccionadas.Contains(o.IdOpcion)) != 1)
            )
            throw new BusinessRuleException("Responde exactamente una opción válida de cada pregunta.");

        var resultados = preguntas.Select(p =>
        {
            var elegida = p.Opciones.Single(o => seleccionadas.Contains(o.IdOpcion));

            return new RespuestaEvaluada(
                p.IdPregunta,
                p.TextoPregunta,
                elegida.IdOpcion,
                elegida.EsCorrecta,
                p.Opciones.Single(o => o.EsCorrecta).TextoOpcion, p.Explicacion);
        }).ToList();

        intento.Aciertos = resultados.Count(r => r.Correcta);

        intento.Porcentaje = Math.Round(100m * intento.Aciertos / preguntas.Count, 1);

        intento.RespuestasRegistro = JsonSerializer.Serialize(resultados);

        intento.FechaEvaluacion = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Resultado(intento);
    }
    public async Task<object> FinalizarAsync(int id, int usuario, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.IntentoCursoVoluntario
            .Where(i => i.Id_Intento == id && i.Id_Usuario == usuario)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.PuntosObtenidos, i => i.PuntosObtenidos), ct);

        var intento = await Buscar(id, usuario, ct);

        if (intento.FechaCompletado != null)
            return new { puntosGanados = intento.PuntosObtenidos, insigniasDesbloqueadas = Array.Empty<string>(), yaFinalizada = true };

        if (intento.FechaEvaluacion == null)
            throw new BusinessRuleException("Primero responde y evalúa el cuestionario.");

        await juego.BloquearUsuarioAsync(usuario, ct);

        var ultimo = await db.IntentoCursoVoluntario
            .Where(i => i.Id_Usuario == usuario && i.IdActividad == intento.IdActividad && i.Exitoso == true)
            .MaxAsync(i => i.FechaCompletado, ct);

        intento.Exitoso = intento.Porcentaje >= intento.AprobacionConfigurada;
        intento.FechaCompletado = DateTime.UtcNow;
        intento.PuntosObtenidos = intento.Exitoso == true && (
            ultimo == null || ultimo <= DateTime.UtcNow.AddDays(-intento.DiasRepeticionConfigurados)
            ) ? intento.PuntosConfigurados : 0;

        var asignaciones = intento.Exitoso == true && intento.IdActividad is { } actividadAprobada
            ? await microlecciones.CompletarAsync(usuario, actividadAprobada, intento.Id_Intento, ct) : [];

        await db.SaveChangesAsync(ct);

        var logros = intento.Exitoso == true ? await juego.ReconocerAsync(
            usuario, $"actividad:{id}", intento.TituloRegistro!, intento.PuntosObtenidos, ct
            ) : Array.Empty<string>();

        // completar un refuerzo asignado tras caer en una simulacion
        var puntosExtra = 0;
        if (asignaciones.Count > 0)
        {
            var puntosRefuerzo = (await juego.ReglasAsync(ct)).PuntosMicroleccion;
            foreach (var asignacion in asignaciones)
            {
                logros = [.. logros, .. await juego.ReconocerAsync(usuario, $"microleccion:{asignacion}", $"Refuerzo completado: {intento.TituloRegistro}", puntosRefuerzo, ct)];
                puntosExtra += puntosRefuerzo;
            }
        }
        await tx.CommitAsync(ct);
        return new { puntosGanados = intento.PuntosObtenidos + puntosExtra, puntosRefuerzo = puntosExtra, insigniasDesbloqueadas = logros, yaFinalizada = false };
    }
}
