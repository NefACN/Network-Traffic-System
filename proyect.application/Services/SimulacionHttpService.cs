using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// datos de una peticion HTTP, ya seudonimizados por la API
public sealed record InteraccionHttpMeta(
    string Ip,
    string Metodo,
    string Ruta,
    int Status,
    long Bytes,
    double DuracionMs,
    string? UserAgent,
    string? Referer);

// exporta las peticiones en formato NGINX para el pipeline
public sealed class SimulacionHttpService(IApplicationDbContext db, MlService ml, IAuditoriaService auditoria)
{
    public const int MaxPorEnvio = 50;
    private static readonly TimeSpan Bolivia = TimeSpan.FromHours(-4);

    public async Task RegistrarAsync(Guid token, string tipo, InteraccionHttpMeta m, CancellationToken ct = default)
    {
        var envio = await db.EnviosSimulacion.AsNoTracking().Where(e => e.TokenUuid == token)
            .Select(e => new { e.Id_Envio, e.Id_Campania }).SingleOrDefaultAsync(ct);
        if (envio is null)
            return;

        // limite por envio con candado: evita que un enlace reutilizado llene la tabla
        await using var propia = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.BloquearAsync(Bloqueos.InteraccionesHttp, envio.Id_Envio, ct);
        if (await db.Set<InteraccionSimulacionHttp>().CountAsync(i => i.IdEnvio == envio.Id_Envio, ct) >= MaxPorEnvio)
            return;

        db.Set<InteraccionSimulacionHttp>().Add(new InteraccionSimulacionHttp {
            IdEnvio = envio.Id_Envio,
            IdCampania = envio.Id_Campania,
            Tipo = tipo,
            Fecha = DateTime.UtcNow,
            Ip = m.Ip,
            Metodo = m.Metodo,
            Ruta = m.Ruta,
            Status = m.Status,
            Bytes = m.Bytes,
            DuracionMs = Math.Round(Math.Max(0, m.DuracionMs), 3),
            UserAgent = Limpiar(m.UserAgent),
            Referer = Limpiar(m.Referer),
        });
        await db.SaveChangesAsync(ct);

        if (propia is not null)
            await propia.CommitAsync(ct);
    }

    // texto seguro dentro de un campo entre comillas del formato combinado de NGINX
    public static string? Limpiar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var sb = new StringBuilder();
        foreach (var c in valor)
        {
            if (c == '"')
                sb.Append("\\x22");
            else if (c == '\\')
                sb.Append("\\x5C");
            else if (!char.IsControl(c))
                sb.Append(c);
            if (sb.Length >= 300)
                break;
        }

        var s = sb.ToString();
        return s.Length > 300 ? s[..300] : s;
    }

    // linea en formato combinado de NGINX compatible con el perfil nginx-request-v1
    public static string LineaNginx(InteraccionSimulacionHttp i)
    {
        var local = i.Fecha.ToUniversalTime() + Bolivia;
        var fecha = local.ToString("dd/MMM/yyyy:HH:mm:ss", CultureInfo.InvariantCulture);
        var segundos = (i.DuracionMs / 1000d).ToString("0.000", CultureInfo.InvariantCulture);

        return $"{i.Ip} - - [{fecha} -0400] \"{i.Metodo} {i.Ruta} HTTP/1.1\" {i.Status} {i.Bytes} \"{i.Referer ?? "-"}\" \"{i.UserAgent ?? "-"}\" {segundos} -";
    }

    // campanias con interacciones HTTP, paginadas
    public async Task<object> CampaniasAsync(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.CampaniasSimulacion.AsNoTracking()
            .Where(c => db.Set<InteraccionSimulacionHttp>().Any(i => i.IdCampania == c.Id_Campania))
            .OrderByDescending(c => c.Id_Campania)
            .Select(c => new {
                idCampania = c.Id_Campania,
                c.Titulo,
                estado = c.Estado.NombreEstado,
                aperturas = db.Set<InteraccionSimulacionHttp>()
                    .Count(i => i.IdCampania == c.Id_Campania && i.Tipo == "apertura"),
                clics = db.Set<InteraccionSimulacionHttp>()
                    .Count(i => i.IdCampania == c.Id_Campania && i.Tipo == "clic"),
                credenciales = db.Set<InteraccionSimulacionHttp>()
                    .Count(i => i.IdCampania == c.Id_Campania && i.Tipo == "credenciales"),
                desde = db.Set<InteraccionSimulacionHttp>()
                    .Where(i => i.IdCampania == c.Id_Campania)
                    .Min(i => (DateTime?)i.Fecha),
                hasta = db.Set<InteraccionSimulacionHttp>()
                    .Where(i => i.IdCampania == c.Id_Campania)
                    .Max(i => (DateTime?)i.Fecha),
            }).PaginarAsync(pagina, tamanio, ct);

    // genera un archivo NGINX con las interacciones de la campania y lo envia a validacion
    public async Task<object> GenerarConjuntoAsync(int idCampania, int actor, CancellationToken ct)
    {
        var campania = await db.CampaniasSimulacion.AsNoTracking()
            .Where(c => c.Id_Campania == idCampania)
            .Select(c => new { c.Titulo })
            .SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("La campaña no existe.", BusinessError.NotFound);
        var interacciones = await db.Set<InteraccionSimulacionHttp>().AsNoTracking()
            .Where(i => i.IdCampania == idCampania)
            .OrderBy(i => i.Fecha)
            .ThenBy(i => i.Id)
            .ToListAsync(ct);
        if (interacciones.Count == 0)
            throw new BusinessRuleException("La campaña todavía no tiene interacciones HTTP registradas.");

        var texto = string.Join("\n", interacciones.Select(LineaNginx)) + "\n";
        using var contenido = new MemoryStream(Encoding.UTF8.GetBytes(texto));
        var (validacion, datasetId) = await ml.UploadWithIdsAsync($"simulacion-campania-{idCampania}.log", contenido, actor, ct);

        var dataset = await db.Set<HttpDataset>().SingleAsync(d => d.Id == datasetId, ct);
        dataset.IdCampaniaSimulacion = idCampania;
        dataset.Name = $"Simulación · {campania.Titulo}"[..Math.Min(120, $"Simulación · {campania.Titulo}".Length)];
        dataset.Origin = $"Tráfico de verdad conocida de la campaña #{idCampania}";
        for (var n = 0; n < interacciones.Count; n++)
            db.Set<LineaSimulacionHttp>().Add(new LineaSimulacionHttp {
                DatasetId = datasetId,
                Linea = n + 1,
                IdInteraccion = interacciones[n].Id
            });

        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("GenerarConjuntoSimulacionHttp", true, actor, entidad: "HttpDataset",
            idEntidadAfectada: datasetId.ToString(), detalles: new { idCampania, lineas = interacciones.Count });

        return new { validationId = validacion, datasetId, lineas = interacciones.Count };
    }

    // conjuntos generados, paginados
    public async Task<object> ConjuntosAsync(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.Set<HttpDataset>().AsNoTracking()
            .Where(d => d.IdCampaniaSimulacion != null)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => new {
                d.Id,
                d.Name,
                d.State,
                d.CreatedAt,
                d.IdCampaniaSimulacion,
                campania = db.CampaniasSimulacion
                    .Where(c => c.Id_Campania == d.IdCampaniaSimulacion)
                    .Select(c => c.Titulo)
                    .FirstOrDefault(),
                lineas = db.Set<LineaSimulacionHttp>().Count(l => l.DatasetId == d.Id),
                ejecuciones = db.Set<MlExecution>()
                    .Where(j => j.DatasetId == d.Id && j.Kind != "validation")
                    .OrderByDescending(j => j.CreatedAt)
                    .Select(j => new { j.Id, j.Kind, j.State, j.ModelVersion, j.CreatedAt })
                    .ToList(),
            }).PaginarAsync(pagina, tamanio, ct);

    // compara lo que hizo el modelo con el origen conocido de cada linea
    public async Task<object> VerdadConocidaAsync(Guid ejecucion, CancellationToken ct)
    {
        var job = await db.Set<MlExecution>().AsNoTracking().SingleOrDefaultAsync(j => j.Id == ejecucion, ct)
            ?? throw new BusinessRuleException("Ejecución no encontrada.", BusinessError.NotFound);
        var dataset = await db.Set<HttpDataset>().AsNoTracking().SingleAsync(d => d.Id == job.DatasetId, ct);
        if (dataset.IdCampaniaSimulacion is null || job.Kind == "validation")
            throw new BusinessRuleException("Selecciona una ejecución de un conjunto generado desde una simulación.");

        var lineas = await (from l in db.Set<LineaSimulacionHttp>().AsNoTracking()
                            join i in db.Set<InteraccionSimulacionHttp>().AsNoTracking() on l.IdInteraccion equals i.Id
                            where l.DatasetId == dataset.Id
                            select new { l.Linea, i.Tipo }).ToListAsync(ct);
        var numeros = lineas.Select(l => l.Linea).ToList();
        var resultados = await db.Set<HttpAnalysisResult>().AsNoTracking()
            .Where(r => r.ExecutionId == ejecucion && numeros.Contains(r.Line))
            .Select(r => new { r.Line, r.Cluster, r.Noise, r.Candidate, r.AnomalyScore })
            .ToDictionaryAsync(r => r.Line, ct);
        static decimal? Tasa(int a, int b) => b > 0 ? Math.Round(100m * a / b, 1) : null;

        var porTipo = lineas.GroupBy(l => l.Tipo).OrderBy(g => g.Key switch { "apertura" => 0, "clic" => 1, _ => 2 }).Select(g =>
        {
            var analizadas = g.Where(l => resultados.ContainsKey(l.Linea)).Select(l => resultados[l.Linea]).ToList();

            return new {
                tipo = g.Key,
                lineas = g.Count(),
                analizadas = analizadas.Count,
                ruido = analizadas.Count(r => r.Noise),
                candidatas = analizadas.Count(r => r.Candidate),
                tasaRuido = Tasa(analizadas.Count(r => r.Noise), analizadas.Count),
                tasaCandidatas = Tasa(analizadas.Count(r => r.Candidate), analizadas.Count),
                scoreMedio = analizadas.Count > 0 ? Math.Round(analizadas.Average(r => r.AnomalyScore), 4) : (double?)null,
                grupos = analizadas
                    .GroupBy(r => r.Cluster)
                    .OrderByDescending(x => x.Count())
                    .Take(5)
                    .Select(x => new { cluster = x.Key, cantidad = x.Count() })
                    .ToList(),
            };
        }).ToList();

        return new {
            ejecucion,
            job.State,
            job.ModelVersion,
            dataset = new { dataset.Id, dataset.Name, dataset.IdCampaniaSimulacion },
            total = lineas.Count,
            analizadas = resultados.Count,
            porTipo,
            nota = "Todas las líneas provienen de personas reales interactuando con una simulación: son tráfico de origen conocido, no ataques. "
                + "La proporción marcada como ruido o alerta candidata describe cómo trata el modelo este comportamiento humano; "
                + "no mide precisión de detección de ataques. Las líneas excluidas o rechazadas por el agente no aparecen como analizadas.",
        };
    }
}
