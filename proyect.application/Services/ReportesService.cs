using System.Globalization;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class ReportesService(IApplicationDbContext db, SeguimientoPreventivoService preventivo)
{
    public async Task<object> CatalogosAsync(CancellationToken ct) => new {
        sucursales = await db.Sucursales.AsNoTracking()
            .OrderBy(s => s.Nombre)
            .Select(s => new { id = s.Id_Sucursal, s.Nombre, s.Activo })
            .ToListAsync(ct),
        areas = await db.AreasOperativas.AsNoTracking()
            .OrderBy(a => a.Nombre)
            .Select(a => new { id = a.Id_Area, a.Nombre, a.Activo })
            .ToListAsync(ct),
        ejecuciones = await db.Set<MlExecution>().AsNoTracking()
            .Where(e => e.Kind != "validation")
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .Select(e => new {
                e.Id,
                e.Kind,
                e.State,
                e.ModelVersion,
                e.From,
                e.To,
                conjunto = db.Set<HttpDataset>().Where(d => d.Id == e.DatasetId).Select(d => d.Name).First()
            })
            .ToListAsync(ct)
    };

    private static string Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null
            ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText())
            : "Sin datos";

    private static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Rate(long n, long d) =>
        d > 0 ? (100m * n / d).ToString("F2", CultureInfo.InvariantCulture) + " %" : "Sin datos";

    public async Task<DocumentoReporte> GenerarAsync(GenerarReporteRequest request, int usuario, CancellationToken ct)
    {
        if (!request.Desde.HasValue || !request.Hasta.HasValue || request.Desde > request.Hasta)
            throw new BusinessRuleException("Selecciona un intervalo de fechas válido.");

        var desde = request.Desde.Value.UtcDateTime;
        var hasta = request.Hasta.Value.UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var criterios = new List<IndicadorReporte>();
        var indicadores = new List<IndicadorReporte>();
        var tablas = new List<TablaReporte>();
        var notas = new List<string>();
        bool parcial;

        if (request.Tipo == "preventivo")
        {
            if (!request.IdSucursal.HasValue)
                throw new BusinessRuleException("Selecciona una sucursal para el reporte preventivo.");

            var data = JsonSerializer.SerializeToElement(
                await preventivo.ConsultarAsync(usuario, true, request.IdSucursal, request.IdArea, desde, hasta, null, ct),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var resumen = data.GetProperty("resumen");
            if (resumen.GetProperty("envios").GetInt32() == 0 && resumen.GetProperty("intentos").GetInt32() == 0)
                throw new BusinessRuleException("No existen resultados preventivos para los criterios seleccionados.");

            criterios.Add(new("Sucursal", Text(data.GetProperty("sucursal"), "nombre")));
            criterios.Add(new("Área", request.IdArea.HasValue
                ? await db.AreasOperativas.Where(a => a.Id_Area == request.IdArea).Select(a => a.Nombre).FirstAsync(ct)
                : "Todas"));
            foreach (var (key, label) in new[] {
                ("campanias", "Campañas"),
                ("participantesCampanias", "Participantes en simulaciones"),
                ("envios", "Envíos aceptados"),
                ("clics", "Clics"),
                ("reportes", "Reportes sospechosos"),
                ("participantesCursos", "Participantes educativos"),
                ("intentos", "Intentos educativos"),
                ("aprobados", "Intentos aprobados")
            })
                indicadores.Add(new(label, Text(resumen, key)));

            var envios = resumen.GetProperty("envios").GetInt32();
            indicadores.Add(new("Tasa de clics", Rate(resumen.GetProperty("clics").GetInt32(), envios)));
            indicadores.Add(new("Tasa de reportes", Rate(resumen.GetProperty("reportes").GetInt32(), envios)));

            void AddTable(string key, string title, string[] headers, string[] keys) => tablas.Add(new(title, headers,
                data.GetProperty(key)
                    .EnumerateArray()
                    .Select(row => (IReadOnlyList<string>)keys.Select(k => Text(row, k)).ToArray())
                    .ToArray()));
            AddTable(
                "campanias",
                "Campañas",
                ["Campaña", "Participantes", "Envíos", "Clics", "Reportes", "Capacitaciones completadas"],
                ["titulo", "participantes", "envios", "clics", "reportes", "capacitacionesCompletadas"]);
            AddTable(
                "actividades",
                "Concientización",
                ["Tema", "Participantes", "Intentos", "Aprobados", "No aprobados", "Pendientes"],
                ["tema", "participantes", "intentos", "aprobados", "noAprobados", "pendientes"]);
            AddTable(
                "indicadoresArea",
                "Áreas",
                ["Área", "Participantes", "Envíos", "Clics", "Tasa de clic", "Reportes", "Tasa de reporte"],
                ["nombre", "participantes", "envios", "clics", "tasaClic", "reportes", "tasaReporte"]);

            parcial = data.GetProperty("actividades").EnumerateArray().Any(a => a.GetProperty("pendientes").GetInt32() > 0) ||
                await db.EnviosSimulacion.AnyAsync(e => e.IdSucursalRegistro == request.IdSucursal
                    && (!request.IdArea.HasValue || e.IdAreaRegistro == request.IdArea)
                    && e.FechaEnvio >= desde
                    && e.FechaEnvio <= hasta
                    && e.Campania.Id_Estado == 2, ct);
            notas.Add(Text(data, "nota"));
            notas.Add("Las campañas se filtran por fecha de envío y el aprendizaje por fecha de inicio. Los estados educativos son los conocidos al generar esta copia. El reporte contiene agregados, sin nombres de colaboradores.");
        }
        else if (request.Tipo == "http")
        {
            if (!request.IdEjecucion.HasValue)
                throw new BusinessRuleException("Selecciona una ejecución HTTP.");

            var run = await db.Set<MlExecution>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.IdEjecucion, ct)
                ?? throw new BusinessRuleException("La ejecución no existe.", BusinessError.NotFound);
            if (run.Kind == "validation")
                throw new BusinessRuleException("Selecciona una ejecución de análisis, no una validación.");

            var q = db.Set<HttpAnalysisResult>().AsNoTracking()
                .Where(r => r.ExecutionId == run.Id && r.OccurredAt >= desde && r.OccurredAt <= hasta);
            if (request.SoloCandidatas)
                q = q.Where(r => r.Candidate);

            var summary = await q
                .GroupBy(r => 1)
                .Select(g => new {
                    total = g.LongCount(),
                    ruido = g.LongCount(r => r.Noise),
                    candidatas = g.LongCount(r => r.Candidate),
                    desde = g.Min(r => r.OccurredAt),
                    hasta = g.Max(r => r.OccurredAt)
                })
                .SingleOrDefaultAsync(ct)
                ?? throw new BusinessRuleException("No existen resultados HTTP para los criterios seleccionados.");

            criterios.Add(new("Ejecución", run.Id.ToString()));
            criterios.Add(new("Modelo", run.ModelVersion ?? "Sin versión"));
            criterios.Add(new("Modalidad", run.Kind));
            criterios.Add(new("Estado", run.State));
            criterios.Add(new("Selección", request.SoloCandidatas ? "Solo candidatas" : "Todos los resultados del intervalo"));
            indicadores.AddRange([
                new("Peticiones incluidas", Number(summary.total)),
                new("Observaciones de ruido", Number(summary.ruido)),
                new("Candidatas a revisión", Number(summary.candidatas)),
                new("Proporción de ruido", Rate(summary.ruido, summary.total))
            ]);

            var groups = await q
                .GroupBy(r => r.Cluster)
                .Select(g => new { grupo = g.Key, total = g.LongCount(), candidatas = g.LongCount(r => r.Candidate) })
                .OrderByDescending(g => g.total)
                .ThenBy(g => g.grupo)
                .Take(500)
                .ToListAsync(ct);
            tablas.Add(new(
                "Agrupamientos",
                ["Grupo", "Peticiones", "Candidatas"],
                groups.Select(g => (IReadOnlyList<string>)new[] { Number(g.grupo), Number(g.total), Number(g.candidatas) }).ToArray()));
            parcial = run.State != "completed";
            notas.Add("Los indicadores incluyen todos los resultados filtrados; la tabla lista hasta 500 grupos. Ruido y candidatas no confirman ataques; los scores no son probabilidades de malicia.");
        }
        else
            throw new BusinessRuleException("Selecciona un tipo de reporte válido.");

        notas.Add("Esta copia conserva los datos observados al generarse. Vuelve a generar para consultar cambios posteriores.");
        var documento = new DocumentoReporte(
            Guid.NewGuid(),
            request.Tipo,
            DateTime.UtcNow,
            desde,
            hasta,
            parcial,
            criterios,
            indicadores,
            tablas,
            notas);

        await tx.CommitAsync(ct);
        return documento;
    }
}
