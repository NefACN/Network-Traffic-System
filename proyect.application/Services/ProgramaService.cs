using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// seguimiento del programa segun los cuatro niveles de Kirkpatrick (ISO/IEC 27001 9.1)
public sealed class ProgramaService(
    IApplicationDbContext db,
    RutasAprendizajeService rutas,
    GobernanzaService gobernanza,
    CyberScoreService cyberScore)
{
    private static decimal? Tasa(int a, int b) => b > 0 ? Math.Round(100m * a / b, 1) : null;

    private static (DateTime Desde, DateTime Hasta) Periodo(DateTime? desde, DateTime? hasta)
    {
        var fin = HoraBolivia.HastaUtc(hasta) ?? DateTime.UtcNow;
        var inicio = HoraBolivia.DesdeUtc(desde) ?? fin.AddDays(-365);
        if (inicio > fin)
            throw new BusinessRuleException("La fecha inicial debe ser anterior a la final.");

        return (inicio, fin);
    }

    public async Task<object> PanelAsync(DateTime? desde, DateTime? hasta, CancellationToken ct)
    {
        var (inicio, fin) = Periodo(desde, hasta);

        // nivel 1 reaccion (encuesta de cultura mas reciente con respuestas)
        var encuesta = await db.Set<EncuestaCultura>().AsNoTracking()
            .Where(e => e.Estado != "borrador" && e.FechaInicio <= fin)
            .OrderByDescending(e => e.FechaInicio)
            .Select(e => new {
                e.Id,
                e.Titulo,
                e.Periodo,
                e.Estado,
                e.Preguntas,
                respuestas = db.Set<RespuestaEncuesta>().Where(r => r.IdEncuesta == e.Id).Select(r => r.Respuestas).ToList()
            }).FirstOrDefaultAsync(ct);
        var umbral = (await gobernanza.PoliticaAsync(ct)).UmbralAgregacion;
        object? nivel1 = null;
        if (encuesta is not null)
        {
            var valores = encuesta.respuestas
                .SelectMany(r => (JsonSerializer.Deserialize<Dictionary<string, int>>(r) ?? []).Values)
                .ToList();
            var visible = encuesta.respuestas.Count >= umbral;
            nivel1 = new {
                encuesta.Id,
                encuesta.Titulo,
                encuesta.Periodo,
                encuesta.Estado,
                respuestas = encuesta.respuestas.Count,
                promedio = visible && valores.Count > 0 ? Math.Round(valores.Average(), 2) : (double?)null,
                favorable = visible && valores.Count > 0
                    ? Math.Round(100.0 * valores.Count(v => v >= 4) / valores.Count, 1)
                    : (double?)null,
                oculto = !visible
            };
        }

        // nivel 2 aprendizaje
        var intentos = await db.IntentoCursoVoluntario.AsNoTracking()
            .Where(i => i.FechaCompletado >= inicio && i.FechaCompletado <= fin && i.IdActividad != null)
            .Select(i => new { i.Exitoso, i.Porcentaje })
            .ToListAsync(ct);
        var porcentajes = intentos.Where(i => i.Porcentaje != null).Select(i => i.Porcentaje!.Value).ToList();
        var cumplimiento = await rutas.EstadosAsync(null, ct);
        var constancias = await db.Set<Constancia>().CountAsync(c => c.FechaEmision >= inicio && c.FechaEmision <= fin, ct);

        // nivel 3 comportamiento observado en simulaciones
        var envios = await db.EnviosSimulacion.AsNoTracking()
            .Where(e => e.EstadoEntrega == "accepted"
                && e.ContextoRegistrado
                && e.FechaEnvio >= inicio
                && e.FechaEnvio <= fin)
            .Select(e => new { e.FechaEnvio, e.HizoClick, e.IngresoCredenciales, e.FechaReporte, e.DificultadNistRegistro })
            .ToListAsync(ct);
        var tendencia = envios
            .GroupBy(e => new DateTime(e.FechaEnvio!.Value.Year, e.FechaEnvio.Value.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key)
            .Select(g => new {
                mes = g.Key,
                envios = g.Count(),
                tasaClic = Tasa(g.Count(e => e.HizoClick), g.Count()),
                tasaCredenciales = Tasa(g.Count(e => e.IngresoCredenciales), g.Count()),
                tasaReporte = Tasa(g.Count(e => e.FechaReporte != null), g.Count())
            }).ToList();

        // nivel 4 resultados en incidentes reales
        var incidentes = await db.Set<IncidenteReportado>().AsNoTracking()
            .Where(i => i.FechaCreacion >= inicio && i.FechaCreacion <= fin)
            .Select(i => new { i.Clasificacion, i.FechaCreacion, i.FechaResolucion })
            .ToListAsync(ct);
        var horas = incidentes
            .Where(i => i.FechaResolucion != null)
            .Select(i => (i.FechaResolucion!.Value - i.FechaCreacion).TotalHours)
            .Order()
            .ToList();
        var anomalias = await db.Set<HttpAnalysisReview>()
            .CountAsync(r => r.State == "incident" && r.UpdatedAt >= inicio && r.UpdatedAt <= fin, ct);

        return new {
            desde = inicio,
            hasta = fin,
            nivel1,
            nivel2 = new {
                actividadesFinalizadas = intentos.Count,
                aprobadas = intentos.Count(i => i.Exitoso == true),
                tasaAprobacion = Tasa(intentos.Count(i => i.Exitoso == true), intentos.Count),
                promedioPorcentaje = porcentajes.Count > 0 ? Math.Round(porcentajes.Average(), 1) : (decimal?)null,
                cumplimientoRutas = Tasa(cumplimiento.Count(i => i.Estado == "al_dia"), cumplimiento.Count),
                requisitosVencidos = cumplimiento.Count(i => i.Estado == "vencido"),
                constancias,
            },
            nivel3 = new {
                envios = envios.Count,
                tasaClic = Tasa(envios.Count(e => e.HizoClick), envios.Count),
                tasaCredenciales = Tasa(envios.Count(e => e.IngresoCredenciales), envios.Count),
                tasaReporte = Tasa(envios.Count(e => e.FechaReporte != null), envios.Count),
                reportesPorClic = envios.Count(e => e.HizoClick) > 0
                    ? Math.Round((decimal)envios.Count(e => e.FechaReporte != null) / envios.Count(e => e.HizoClick), 2)
                    : (decimal?)null,
                tendencia,
            },
            nivel4 = new {
                incidentesReportados = incidentes.Count,
                maliciososConfirmados = incidentes.Count(i => i.Clasificacion == "malicioso"),
                medianaHorasResolucion = horas.Count > 0
                    ? Math.Round(
                        horas.Count % 2 == 1 ? horas[horas.Count / 2] : (horas[horas.Count / 2 - 1] + horas[horas.Count / 2]) / 2,
                        1)
                    : (double?)null,
                anomaliasHttpConfirmadas = anomalias,
            },
            nota = "Niveles de Kirkpatrick: 1 reacción (encuesta), 2 aprendizaje (actividades y rutas), 3 comportamiento observado (simulaciones), 4 resultados (incidentes reales). Indicadores descriptivos: no establecen causalidad.",
        };
    }

    public sealed record ControlMatriz(
        string Norma,
        string Control,
        string Requisito,
        string Funcionalidad,
        string Pantalla,
        string Evidencia);

    public static readonly IReadOnlyList<ControlMatriz> Matriz =
    [
        new(
            "ISO/IEC 27001:2022",
            "5.1 / 5.2",
            "Liderazgo y política",
            "Política ética de simulaciones versionada y aprobación en dos pasos",
            "gobernanza.html",
            "politica"),
        new(
            "ISO/IEC 27001:2022",
            "7.2",
            "Competencia y evidencia documentada",
            "Rutas de aprendizaje por área, inducción, refrescos y constancias verificables",
            "programa.html",
            "constancias"),
        new(
            "ISO/IEC 27001:2022",
            "7.3",
            "Concientización",
            "Actividades, microlecciones, aprendizaje en el momento y compromiso no punitivo",
            "concientizacion-admin.html",
            "actividades"),
        new("ISO/IEC 27001:2022", "7.4", "Comunicación", "Planificador anual con temporadas de riesgo", "programa.html", "plan"),
        new(
            "ISO/IEC 27001:2022",
            "9.1",
            "Seguimiento, medición y evaluación",
            "Panel Kirkpatrick, prueba de McNemar y CyberScore v2",
            "programa.html",
            "campanias"),
        new(
            "ISO/IEC 27002:2022",
            "6.3",
            "Concientización, educación y formación",
            "Simulaciones calificadas con NIST Phish Scale y microlecciones",
            "campanias-phishing.html",
            "campanias"),
        new(
            "ISO/IEC 27002:2022",
            "6.8",
            "Reporte de eventos de seguridad",
            "Reporte de simulaciones y de mensajes reales por cualquier canal",
            "dashboard-usuario.html",
            "reportes"),
        new(
            "ISO/IEC 27002:2022",
            "5.24–5.26",
            "Gestión y respuesta a incidentes",
            "Cola de incidentes con prioridad, clasificación y respuesta",
            "incidentes.html",
            "incidentes"),
        new(
            "ISO/IEC 27002:2022",
            "5.27",
            "Aprendizaje de los incidentes",
            "Sugerencias de concientización desde incidentes y anomalías HTTP",
            "http-ciclo.html",
            "sugerencias"),
        new(
            "ISO/IEC 27002:2022",
            "5.34",
            "Privacidad y protección de datos personales",
            "Seudonimización HTTP, señuelo sin captura, encuesta anónima y umbral de agregación",
            "gobernanza.html",
            "politica"),
        new(
            "ISO/IEC 27002:2022",
            "8.16",
            "Actividades de monitoreo",
            "Análisis HTTP con HDBSCAN y tráfico de verdad conocida",
            "http-analisis.html",
            "http"),
        new(
            "NIST SP 800-50 Rev. 1",
            "Medición",
            "Medir el cambio de comportamiento, no solo la finalización",
            "Tasas de clic, credenciales y reporte por dificultad y antes/después",
            "resultados-campanias.html",
            "campanias"),
        new(
            "NIST SP 800-50 Rev. 1",
            "Cultura",
            "Evaluar la cultura de seguridad",
            "Encuesta Likert anónima con comparación entre periodos",
            "programa.html",
            "encuestas"),
        new(
            "NIST TN 2276",
            "Phish Scale",
            "Dificultad de detección de los mensajes",
            "Señales de alerta y alineación por área",
            "campanias-phishing.html",
            "plantillas"),
    ];

    private async Task<Dictionary<string, int>> ConteosAsync(CancellationToken ct) => new() {
        ["politica"] = (await gobernanza.PoliticaAsync(ct)).Revision,
        ["constancias"] = await db.Set<Constancia>().CountAsync(ct),
        ["actividades"] = await db.Set<ActividadEducativa>().CountAsync(a => a.Habilitada, ct),
        ["plan"] = await db.Set<EventoPlan>().CountAsync(e => e.Estado != "cancelado", ct),
        ["campanias"] = await db.CampaniasSimulacion.CountAsync(c => c.FechaFinalizacion != null, ct),
        ["reportes"] = await db.EnviosSimulacion.CountAsync(e => e.FechaReporte != null, ct)
            + await db.Set<IncidenteReportado>().CountAsync(ct),
        ["incidentes"] = await db.Set<IncidenteReportado>().CountAsync(i => i.Estado == "resuelto", ct),
        ["sugerencias"] = await db.Set<SugerenciaConcientizacion>().CountAsync(ct),
        ["http"] = await db.Set<MlExecution>().CountAsync(j => j.Kind != "validation" && j.State == "completed", ct),
        ["encuestas"] = await db.Set<EncuestaCultura>().CountAsync(e => e.Estado != "borrador", ct),
        ["plantillas"] = await db.PlantillasPhishing.CountAsync(p => p.Senales.Any(), ct),
    };

    public static readonly IReadOnlyDictionary<string, string> EtiquetasEvidencia = new Dictionary<string, string> {
        ["politica"] = "revisiones de la política",
        ["constancias"] = "constancias emitidas",
        ["actividades"] = "actividades habilitadas",
        ["plan"] = "eventos planificados",
        ["campanias"] = "campañas cerradas",
        ["reportes"] = "reportes registrados",
        ["incidentes"] = "incidentes resueltos",
        ["sugerencias"] = "sugerencias generadas",
        ["http"] = "análisis HTTP completados",
        ["encuestas"] = "encuestas aplicadas",
        ["plantillas"] = "plantillas calificadas",
    };

    public async Task<object> MatrizAsync(CancellationToken ct)
    {
        var conteos = await ConteosAsync(ct);

        return Matriz.Select(m => new {
            m.Norma,
            m.Control,
            m.Requisito,
            m.Funcionalidad,
            m.Pantalla,
            evidencias = conteos[m.Evidencia],
            descripcionEvidencia = EtiquetasEvidencia[m.Evidencia],
            conEvidencia = conteos[m.Evidencia] > 0
        }).ToList();
    }

    // evidencias agregadas para auditoria, sin respuestas ni datos individuales sensibles
    public async Task<object> EvidenciasAsync(DateTime? desde, DateTime? hasta, int actor, CancellationToken ct)
    {
        var (inicio, fin) = Periodo(desde, hasta);
        var politica = await gobernanza.PoliticaAsync(ct);

        // solo agregados de cumplimiento, la lista nominal de atrasos queda en el sistema
        var cumplimiento = JsonSerializer.SerializeToElement(await rutas.CumplimientoAsync(ct));

        return new {
            generadoEn = DateTime.UtcNow,
            generadoPor = actor,
            periodo = new { desde = inicio, hasta = fin },
            matriz = await MatrizAsync(ct),
            panel = await PanelAsync(inicio, fin, ct),
            politicaEtica = new {
                politica.Revision,
                politica.ActualizadaEn,
                politica.RequiereAprobacion,
                politica.VentanaActiva,
                politica.HoraInicio,
                politica.HoraFin,
                politica.MaxSimulacionesPorPersona,
                politica.UmbralAgregacion,
                pretextosProhibidos = GobernanzaService.Terminos(politica)
            },
            politicaCyberScore = await cyberScore.PoliticaAsync(ct),
            campanias = await db.CampaniasSimulacion.AsNoTracking()
                .Where(c => c.FechaFinalizacion >= inicio && c.FechaFinalizacion <= fin)
                .Select(c => new {
                    c.Id_Campania,
                    c.Titulo,
                    c.TipoEvaluacion,
                    c.EstadoAprobacion,
                    c.FechaRevision,
                    aprobador = db.Users.Where(u => u.Id == c.RevisadaPor).Select(u => u.Nombre + " " + u.Apellido).FirstOrDefault(),
                    c.FechaInicioProgramada,
                    c.FechaFinalizacion,
                    envios = c.Envios.Count(e => e.EstadoEntrega == "accepted")
                }).ToListAsync(ct),
            rutas = new {
                total = cumplimiento.GetProperty("total").GetInt32(),
                cumplimiento = cumplimiento.GetProperty("cumplimiento"),
                porRuta = cumplimiento.GetProperty("porRuta"),
                porArea = cumplimiento.GetProperty("porArea")
            },
            constancias = await db.Set<Constancia>().AsNoTracking()
                .Where(c => c.FechaEmision >= inicio && c.FechaEmision <= fin)
                .GroupBy(c => c.Actividad)
                .Select(g => new { actividad = g.Key, emitidas = g.Count() })
                .ToListAsync(ct),
            plan = await db.Set<EventoPlan>().AsNoTracking()
                .Where(e => e.Anio >= inicio.Year && e.Anio <= fin.Year)
                .Select(e => new { e.Anio, e.Mes, e.Titulo, e.Tipo, e.Estado })
                .OrderBy(e => e.Anio)
                .ThenBy(e => e.Mes)
                .ToListAsync(ct),
            nota = "Paquete generado por DISMAC. Contiene agregados y decisiones registradas; los resultados individuales permanecen en el sistema con acceso restringido.",
        };
    }
}
