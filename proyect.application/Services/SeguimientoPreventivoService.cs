using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;

namespace proyect.application.Services;

// datos internos y reglas descriptivas, no son inferencias del modelo HTTP
public sealed class SeguimientoPreventivoService(
    IApplicationDbContext db,
    GobernanzaService gobernanza,
    CyberScoreColaboradoresService cyberScoreAreas)
{
    public async Task<object> ConsultarAsync(int usuarioId, bool admin, int? sucursalId, int? areaId,
        DateTime? desde, DateTime? hasta, int? campaniaId, CancellationToken ct)
    {
        var fin = HoraBolivia.HastaUtc(hasta) ?? DateTime.UtcNow;
        var inicio = HoraBolivia.DesdeUtc(desde) ?? fin.AddDays(-90);
        if (inicio > fin)
            throw new BusinessRuleException("La fecha inicial debe ser anterior a la final.");

        var usuario = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId && u.Activo, ct)
            ?? throw new BusinessRuleException("Usuario no disponible.", BusinessError.Forbidden);
        var alcance = admin ? sucursalId ?? usuario.Id_Sucursal : usuario.Id_Sucursal;
        if (!admin && sucursalId.HasValue && sucursalId != alcance)
            throw new BusinessRuleException("Solo puedes consultar tu sucursal asignada.", BusinessError.Forbidden);
        if (!alcance.HasValue)
            throw new BusinessRuleException("Se requiere una sucursal asignada para consultar el seguimiento preventivo.", BusinessError.Forbidden);

        var sucursal = await db.Sucursales.AsNoTracking()
            .Where(s => s.Id_Sucursal == alcance && (admin || s.Activo))
            .Select(s => new { idSucursal = s.Id_Sucursal, s.Nombre })
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("La sucursal no está disponible.", BusinessError.Forbidden);

        var miembros = db.Users.AsNoTracking().Where(u => u.Id_Sucursal == alcance);
        var historicoEnvios = db.EnviosSimulacion.AsNoTracking()
            .Where(e => e.ContextoRegistrado && e.IdSucursalRegistro == alcance);
        var historicoCursos = db.IntentoCursoVoluntario.AsNoTracking()
            .Where(i => i.ContextoRegistrado && i.IdSucursalRegistro == alcance);
        var areas = await db.AreasOperativas.AsNoTracking()
            .Where(a => miembros.Any(u => u.Id_Area == a.Id_Area)
                || historicoEnvios.Any(e => e.IdAreaRegistro == a.Id_Area)
                || historicoCursos.Any(i => i.IdAreaRegistro == a.Id_Area))
            .OrderBy(a => a.Nombre)
            .Select(a => new { idArea = a.Id_Area, a.Nombre, criticidadNegocio = a.Criticidad.Nivel })
            .ToListAsync(ct);
        if (areaId.HasValue && !areas.Any(a => a.idArea == areaId))
            throw new BusinessRuleException("El área no pertenece a tu sucursal ni a su historial registrado.", BusinessError.Forbidden);

        if (areaId.HasValue)
        {
            historicoEnvios = historicoEnvios.Where(e => e.IdAreaRegistro == areaId);
            historicoCursos = historicoCursos.Where(i => i.IdAreaRegistro == areaId);
            miembros = miembros.Where(u => u.Id_Area == areaId);
        }

        var envios = historicoEnvios.Where(e => e.IdAreaRegistro != null && e.IdCategoriaRegistro != null && e.EstadoEntrega == "accepted"
            && e.FechaEnvio >= inicio && e.FechaEnvio <= fin);
        var cursos = historicoCursos.Where(i => i.IdAreaRegistro != null && i.FechaInicio >= inicio && i.FechaInicio <= fin);
        var campanias = await envios.GroupBy(e => new { e.Id_Campania, e.Campania.Titulo })
            .Select(g => new {
                idCampania = g.Key.Id_Campania,
                g.Key.Titulo,
                fecha = g.Min(e => e.FechaEnvio),
                participantes = g.Select(e => e.Id_Usuario).Distinct().Count(),
                envios = g.Count(),
                aperturas = g.Count(e => e.FueAbierto),
                clics = g.Count(e => e.HizoClick),
                reportes = g.Count(e => e.FechaReporte != null),
                reportesSinClickPrevio = g.Count(e => e.FechaReporte != null && e.ReporteSinClickPrevio),
                capacitacionesCompletadas = g.Count(e => e.HizoClick && e.CapacitacionCompleta)
            })
            .OrderByDescending(c => c.fecha).ToListAsync(ct);
        if (campaniaId.HasValue && !campanias.Any(c => c.idCampania == campaniaId))
            throw new BusinessRuleException("No hay resultados disponibles para esa campaña en el alcance seleccionado.", BusinessError.NotFound);
        if (campaniaId.HasValue)
            envios = envios.Where(e => e.Id_Campania == campaniaId);

        var seleccion = campanias.Where(c => !campaniaId.HasValue || c.idCampania == campaniaId).ToList();
        var actividades = await cursos.GroupBy(i => new { i.Id_Categoria, i.Categoria.Nombre })
            .Select(g => new {
                idCategoria = g.Key.Id_Categoria,
                tema = g.Key.Nombre,
                intentos = g.Count(),
                participantes = g.Select(i => i.Id_Usuario).Distinct().Count(),
                aprobados = g.Count(i => i.Exitoso == true),
                noAprobados = g.Count(i => i.Exitoso == false),
                pendientes = g.Count(i => i.Exitoso == null)
            }).OrderBy(a => a.tema).ToListAsync(ct);
        var refuerzoPhishing = await envios.Where(e => e.HizoClick).GroupBy(e => e.IdCategoriaRegistro)
            .Select(g => new {
                idCategoria = g.Key,
                tema = db.CategoriasPregunta.Where(c => c.IdCategoria == g.Key).Select(c => c.Nombre).First(),
                clics = g.Count(),
                pendientes = g.Count(e => !e.CapacitacionCompleta)
            }).ToListAsync(ct);
        var porArea = await envios.GroupBy(e => e.IdAreaRegistro).Select(g => new {
            idArea = g.Key,
            envios = g.Count(),
            participantes = g.Select(e => e.Id_Usuario).Distinct().Count(),
            clics = g.Count(e => e.HizoClick),
            personasConClic = g.Where(e => e.HizoClick).Select(e => e.Id_Usuario).Distinct().Count(),
            reportes = g.Count(e => e.FechaReporte != null),
            reportesSinClickPrevio = g.Count(e => e.FechaReporte != null && e.ReporteSinClickPrevio),
            pendientes = g.Count(e => e.HizoClick && !e.CapacitacionCompleta),
            completadas = g.Count(e => e.HizoClick && e.CapacitacionCompleta),
            ultimoEnvio = g.Max(e => e.FechaEnvio)
        }).ToListAsync(ct);
        var repeticiones = await envios.Where(e => e.HizoClick).GroupBy(e => new { e.IdAreaRegistro, e.Id_Usuario })
            .Where(g => g.Select(e => e.Id_Campania).Distinct().Count() > 1)
            .Select(g => new { g.Key.IdAreaRegistro }).ToListAsync(ct);
        var cursosPorArea = await cursos.GroupBy(i => i.IdAreaRegistro).Select(g => new {
            idArea = g.Key,
            intentos = g.Count(),
            aprobados = g.Count(i => i.Exitoso == true),
            noAprobados = g.Count(i => i.Exitoso == false)
        }).ToListAsync(ct);

        // series descriptivas del mismo tema y dificultad, sin atribuir causalidad
        var series = await envios
            .GroupBy(e => new { e.IdAreaRegistro, e.Id_Campania, e.Campania.Titulo, e.IdCategoriaRegistro, e.Campania.NivelDificultad })
            .Select(g => new {
                idArea = g.Key.IdAreaRegistro,
                idCampania = g.Key.Id_Campania,
                g.Key.Titulo,
                idCategoria = g.Key.IdCategoriaRegistro,
                dificultad = g.Key.NivelDificultad,
                fecha = g.Min(e => e.FechaEnvio),
                envios = g.Count(),
                participantes = g.Select(e => e.Id_Usuario).Distinct().Count(),
                clics = g.Count(e => e.HizoClick)
            }).ToListAsync(ct);

        // el gerente no ve areas por debajo del umbral de agregacion
        var umbralAreas = admin ? 0 : (await gobernanza.PoliticaAsync(ct)).UmbralAgregacion;
        var cyberAreas = await cyberScoreAreas.PorAreaAsync(alcance, umbralAreas, ct);
        var indicadores = areas.Where(a => !areaId.HasValue || a.idArea == areaId).Select(a =>
        {
            var r = porArea.FirstOrDefault(r => r.idArea == a.idArea);
            var c = cursosPorArea.FirstOrDefault(c => c.idArea == a.idArea);
            var reincidentes = repeticiones.Count(r => r.IdAreaRegistro == a.idArea);
            var evaluacion = PoliticaPreventiva.Evaluar(r?.envios ?? 0, r?.participantes ?? 0, r?.clics ?? 0,
                r?.pendientes ?? 0, reincidentes, r?.ultimoEnvio, DateTime.UtcNow);

            return new {
                a.idArea,
                a.Nombre,
                a.criticidadNegocio,
                envios = r?.envios ?? 0,
                participantes = r?.participantes ?? 0,
                clics = r?.clics ?? 0,
                personasConClic = r?.personasConClic ?? 0,
                tasaClic = PoliticaPreventiva.Tasa(r?.clics ?? 0, r?.envios ?? 0),
                reincidentes,
                reportes = r?.reportes ?? 0,
                reportesSinClickPrevio = r?.reportesSinClickPrevio ?? 0,
                tasaReporte = PoliticaPreventiva.Tasa(r?.reportes ?? 0, r?.envios ?? 0),
                capacitacionesPendientes = r?.pendientes ?? 0,
                capacitacionesCompletadas = r?.completadas ?? 0,
                intentosEducativos = c?.intentos ?? 0,
                aprobados = c?.aprobados ?? 0,
                noAprobados = c?.noAprobados ?? 0,
                ultimoEnvio = r?.ultimoEnvio,
                evaluacion,
                cyberScore = cyberAreas.FirstOrDefault(x => x.IdArea == a.idArea),
                evolucion = series
                    .Where(s => s.idArea == a.idArea)
                    .GroupBy(s => new { s.idCategoria, s.dificultad })
                    .Where(g => g.Count() > 1)
                    .Select(g =>
                    {
                        var ordered = g.OrderByDescending(s => s.fecha).ThenByDescending(s => s.idCampania).Take(2).ToArray();
                        var actual = ordered[0];
                        var anterior = ordered[1];

                        return new {
                            g.Key.idCategoria,
                            g.Key.dificultad,
                            actual = actual.Titulo,
                            anterior = anterior.Titulo,
                            tasaActual = PoliticaPreventiva.Tasa(actual.clics, actual.envios),
                            tasaAnterior = PoliticaPreventiva.Tasa(anterior.clics, anterior.envios),
                            diferenciaPuntos = PoliticaPreventiva.Tasa(actual.clics, actual.envios)
                                - PoliticaPreventiva.Tasa(anterior.clics, anterior.envios),
                            muestraSuficiente = actual.envios >= 10
                                && anterior.envios >= 10
                                && actual.participantes >= 5
                                && anterior.participantes >= 5
                        };
                    }).ToList()
            };
        }).ToList();

        // confidencialidad: el gerente no ve grupos menores al umbral
        var umbral = admin ? 0 : (await gobernanza.PoliticaAsync(ct)).UmbralAgregacion;
        var participantesCampanias = await envios.Select(e => e.Id_Usuario).Distinct().CountAsync(ct);
        var ocultarResumen = participantesCampanias < umbral;

        return new {
            sucursal,
            areas,
            desde = inicio,
            hasta = fin,
            areaId,
            campaniaId,
            umbralAgregacion = admin ? (int?)null : umbral,
            resumen = ocultarResumen
                ? (object)new { colaboradores = await miembros.CountAsync(ct), suprimido = true, campanias = seleccion.Count }
                : new {
                    colaboradores = await miembros.CountAsync(ct),
                    campanias = seleccion.Count,
                    participantesCampanias,
                    envios = seleccion.Sum(c => c.envios),
                    clics = seleccion.Sum(c => c.clics),
                    capacitacionesCompletadas = seleccion.Sum(c => c.capacitacionesCompletadas),
                    reportes = seleccion.Sum(c => c.reportes),
                    reportesSinClickPrevio = seleccion.Sum(c => c.reportesSinClickPrevio),
                    participantesCursos = await cursos.Select(i => i.Id_Usuario).Distinct().CountAsync(ct),
                    intentos = actividades.Sum(a => a.intentos),
                    aprobados = actividades.Sum(a => a.aprobados)
                },
            campanias = campanias.Select(c => c.participantes < umbral
                ? (object)new { c.idCampania, c.Titulo, c.fecha, suprimido = true, participantes = (int?)null } : c).ToList(),
            actividades = actividades
                .Select(a => a.participantes < umbral ? (object)new { a.idCategoria, a.tema, suprimido = true } : a)
                .ToList(),
            indicadoresArea = indicadores.Select(a => a.participantes > 0 && a.participantes < umbral
                ? (object)new {
                    a.idArea,
                    a.Nombre,
                    a.criticidadNegocio,
                    suprimido = true,
                    envios = (int?)null,
                    participantes = (int?)null,
                    evaluacion = new EvaluacionPreventiva(
                        "Grupo reducido",
                        "Sin evaluar",
                        [$"Se ocultan grupos de menos de {umbral} personas para proteger la confidencialidad."]),
                    evolucion = Array.Empty<object>()
                } : a).ToList(),
            temasRefuerzo = actividades
                .Where(a => a.noAprobados > 0 && a.participantes >= umbral)
                .OrderByDescending(a => a.noAprobados),
            refuerzoPhishing = ocultarResumen ? [] : refuerzoPhishing.Cast<object>().ToList(),
            calidadDatos = new {
                enviosSinContexto = await db.EnviosSimulacion.CountAsync(e =>
                    (!e.ContextoRegistrado || e.IdSucursalRegistro == null || e.IdAreaRegistro == null)
                    && miembros.Any(u => u.Id == e.Id_Usuario), ct),
                cursosSinContexto = await db.IntentoCursoVoluntario.CountAsync(i =>
                    (!i.ContextoRegistrado || i.IdSucursalRegistro == null || i.IdAreaRegistro == null)
                    && miembros.Any(u => u.Id == i.Id_Usuario), ct),
                enviosSinConfirmar = await historicoEnvios.CountAsync(e => e.EstadoEntrega != "accepted" &&
                    (e.FechaEnvio == null || (e.FechaEnvio >= inicio && e.FechaEnvio <= fin)), ct)
            },
            politica = new {
                version = PoliticaPreventiva.Version,
                minimoEnvios = 10,
                minimoParticipantes = 5,
                vigenciaDias = 90,
                descripcion = "Prioridad de refuerzo por reglas: alta con tasa de clics desde 30 % o reincidencia; media desde 10 % o capacitación pendiente; baja en los demás casos. Solo se clasifica con muestra suficiente y actividad reciente. No es una probabilidad de ataque ni una predicción de IA."
            },
            nota = "Sucursal y área conservadas al enviar cada simulación o iniciar cada curso. Se incluyen cuentas inactivas con historial. El periodo de campañas usa la fecha real de envío; los resultados reflejan su estado actual. Por defecto se consultan 90 días. El historial antiguo sin contexto queda excluido, sin atribuirle una ubicación supuesta. Los cursos se evalúan por tema; no se guardan errores por pregunta. Las comparaciones son descriptivas: no demuestran por sí solas eficacia causal de la capacitación."
        };
    }
}

public sealed record EvaluacionPreventiva(string EstadoDatos, string Prioridad, string[] Recomendaciones);

public static class PoliticaPreventiva
{
    public const string Version = "preventivo-v1";

    public static decimal? Tasa(int positivos, int total) => total > 0 ? Math.Round(100m * positivos / total, 2) : null;

    public static EvaluacionPreventiva Evaluar(
        int envios,
        int participantes,
        int clics,
        int pendientes,
        int reincidentes,
        DateTime? ultimo,
        DateTime ahora)
    {
        var acciones = new List<string>();
        if (pendientes > 0)
            acciones.Add($"Completar {pendientes} capacitaciones pendientes de las simulaciones.");
        if (reincidentes > 0)
            acciones.Add("Reforzar los temas de las campañas con clics repetidos y programar una nueva simulación de seguimiento.");

        if (envios == 0)
            return new("Sin datos", "Sin evaluar", ["Programar una simulación para esta sucursal y área."]);
        if (ultimo is null || ultimo < ahora.AddDays(-90))
            return new("Desactualizado", "Sin evaluar", acciones
                .Append("Realizar una nueva simulación antes de evaluar la situación actual.")
                .ToArray());
        if (envios < 10 || participantes < 5)
            return new("Muestra limitada", "Sin evaluar", acciones
                .Append("Ampliar la muestra: se requieren al menos 10 envíos y 5 participantes distintos.")
                .ToArray());

        var tasa = Tasa(clics, envios)!.Value;
        var prioridad = tasa >= 30 || reincidentes > 0 ? "Alta" : tasa >= 10 || pendientes > 0 ? "Media" : "Baja";
        acciones.Add(prioridad == "Baja"
            ? "Mantener actividades periódicas y verificar la evolución en próximas campañas."
            : "Priorizar actividades de concientización en los temas con mayores dificultades y medir nuevamente con una campaña del mismo tema y dificultad.");

        return new("Datos recientes", prioridad, acciones.ToArray());
    }
}
