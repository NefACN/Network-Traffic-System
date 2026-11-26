using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class GamificacionService(IApplicationDbContext db, CyberScoreService cyberScore)
{
    public Task<ReglasGamificacion> ReglasAsync(CancellationToken ct = default) => db.Set<ReglasGamificacion>().AsNoTracking().SingleAsync(ct);
    public Task BloquearUsuarioAsync(int id, CancellationToken ct = default) => db.Users.Where(u => u.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(u => u.PuntosEducativos, u => u.PuntosEducativos), ct);

    // el llamador mantiene la transaccion y bloquea al usuario antes de comprobar enfriamientos
    public async Task<string[]> ReconocerAsync(int idUsuario, string clave, string concepto, int puntos, CancellationToken ct = default)
    {
        if (puntos < 0 || puntos > 10000)
            throw new BusinessRuleException("Puntuación fuera de los límites permitidos.");

        if (puntos > 0 && !await db.Set<MovimientoPuntos>().AnyAsync(m => m.IdUsuario == idUsuario && m.Clave == clave, ct))
        {
            db.Set<MovimientoPuntos>().Add(new MovimientoPuntos {
                IdUsuario = idUsuario,
                Clave = clave,
                Concepto = concepto[..Math.Min(200, concepto.Length)],
                Puntos = puntos
            });

            await db.Users.Where(u => u.Id == idUsuario).ExecuteUpdateAsync(s => s.SetProperty(u => u.PuntosEducativos, u => u.PuntosEducativos + puntos), ct);
            await db.SaveChangesAsync(ct);
        }

        var puntosActuales = await db.Users.Where(u => u.Id == idUsuario).Select(u => u.PuntosEducativos).SingleAsync(ct);

        var m = await MetricasAsync(idUsuario, puntosActuales, ct);
        var obtenidas = await db.LogrosUsuario.Where(l => l.Id_Usuario == idUsuario).Select(l => l.Id_Insignia).ToListAsync(ct);
        var disponibles = await db.Insignias.Where(i => i.Activa && !obtenidas.Contains(i.Id_Insignia)).ToListAsync(ct);

        var nuevas = disponibles.Where(i => Valor(i.TipoCondicion, m) >= i.PuntosRequeridos).ToList();

        foreach (var logro in nuevas)
            db.LogrosUsuario.Add(new LogroUsuario {
                Id_Usuario = idUsuario,
                Id_Insignia = logro.Id_Insignia,
                FechaObtencion = DateTime.UtcNow
            });

        await db.SaveChangesAsync(ct);

        return nuevas.Select(i => i.Nombre).ToArray();
    }

    // valores con los que se evaluan los logros
    public sealed record Metricas(int Puntos, int Actividades, int Temas, int Semanas, int Reportes, int CyberScore);
    public static int Valor(string condicion, Metricas m) => condicion switch {
        "puntos" => m.Puntos,
        "actividades" => m.Actividades,
        "temas" => m.Temas,
        "semanas" => m.Semanas,
        "reportes" => m.Reportes,
        "cyberscore" => m.CyberScore,
        _ => 0
    };

    public static int Valor(string condicion, int puntos, int actividades, int temas, int semanas) => Valor(condicion, new Metricas(puntos, actividades, temas, semanas, 0, 0));
    private async Task<Metricas> MetricasAsync(int usuario, int puntos, CancellationToken ct)
    {
        var completadas = db.IntentoCursoVoluntario.Where(i => i.Id_Usuario == usuario && i.FechaCompletado != null && i.Exitoso == true);

        var total = await completadas.Where(i => i.IdActividad != null).Select(i => i.IdActividad).Distinct().CountAsync(ct)
            + await completadas.Where(i => i.IdActividad == null).Select(i => i.Id_Categoria).Distinct().CountAsync(ct);

        var temas = await completadas.Select(i => i.Id_Categoria).Distinct().CountAsync(ct);
        var fechas = await completadas.Select(i => i.FechaCompletado!.Value).ToListAsync(ct);
        var reportes = await db.EnviosSimulacion.CountAsync(e => e.Id_Usuario == usuario && e.FechaReporte != null && e.EstadoEntrega == "accepted", ct);

        // solo un CyberScore evaluable (muestra minima) puede desbloquear logros por CyberScore
        var score = await cyberScore.ResultadoAsync(usuario, ct);
        return new Metricas(
            puntos,
            total,
            temas,
            fechas.Select(InicioSemana).Distinct().Count(),
            reportes,
            score.CyberScore is { } valor ? (int)Math.Floor(valor) : 0
            );
    }

    // la semana de aprendizaje comienza los lunes
    public static DateTime InicioSemana(DateTime utc)
    {
        var local = utc.ToUniversalTime().AddHours(-4).Date;
        return DateTime.SpecifyKind(local.AddDays(-((int)local.DayOfWeek + 6) % 7).AddHours(4), DateTimeKind.Utc);
    }

    public async Task<object> ProgresoAsync(int usuario, CancellationToken ct)
    {
        var reglas = await ReglasAsync(ct);
        var puntos = await db.Users.Where(u => u.Id == usuario).Select(u => u.PuntosEducativos).SingleAsync(ct);
        var completadas = db.IntentoCursoVoluntario.AsNoTracking().Where(i => i.Id_Usuario == usuario && i.FechaCompletado != null);
        var aprobadas = completadas.Where(i => i.Exitoso == true);
        var m = await MetricasAsync(usuario, puntos, ct);
        int total = m.Actividades, temas = m.Temas, semanas = m.Semanas;

        var inicio = InicioSemana(DateTime.UtcNow);
        var semana = await aprobadas.Where(i => i.FechaCompletado >= inicio).Select(i => i.Id_Categoria).Distinct().CountAsync(ct);
        var temasDisponibles = await db.CategoriasPregunta.CountAsync(c => db.Set<ActividadEducativa>().Any(a => a.Habilitada && a.IdCategoria == c.IdCategoria)
            || db.PregutasConcientizacion.Any(p => p.Id_Categoria == c.IdCategoria && p.Opciones.Count >= 2 && p.Opciones.Count(o => o.EsCorrecta) == 1), ct);
        var objetivoSemanal = temasDisponibles > 0 ? Math.Min(reglas.MetaSemanalTemas, temasDisponibles) : reglas.MetaSemanalTemas;

        var logros = await db.Insignias.AsNoTracking().Where(i => i.Activa).Select(i => new {
            i.Id_Insignia,
            i.Nombre,
            i.Descripcion,
            i.TipoCondicion,
            objetivo = i.PuntosRequeridos,
            obtenido = db.LogrosUsuario.Any(l => l.Id_Usuario == usuario && l.Id_Insignia == i.Id_Insignia)
        }).ToListAsync(ct);

        var historial = await completadas.OrderByDescending(i => i.FechaCompletado).Take(100).Select(i => new {
            idIntento = i.Id_Intento,
            i.IdActividad,
            titulo = i.TituloRegistro ?? i.Categoria.Nombre,
            tema = i.Categoria.Nombre,
            i.FechaCompletado,
            i.Exitoso,
            i.Porcentaje,
            i.PuntosObtenidos
        }).ToListAsync(ct);

        var enCurso = await db.IntentoCursoVoluntario.AsNoTracking().Where(i => i.Id_Usuario == usuario && i.IdActividad != null && i.FechaCompletado == null)
            .Select(i => new {
                idIntento = i.Id_Intento,
                titulo = i.TituloRegistro,
                i.FechaInicio,
                evaluado = i.FechaEvaluacion != null
            }).ToListAsync(ct);

        var movimientos = await db.Set<MovimientoPuntos>().AsNoTracking().Where(m => m.IdUsuario == usuario).OrderByDescending(m => m.Fecha).Take(100)
            .Select(m => new {
                m.Concepto,
                m.Puntos,
                m.Fecha
            }).ToListAsync(ct);

        var since = DateTime.UtcNow.AddDays(-90);

        var evidencias = await db.EnviosSimulacion.AsNoTracking().Where(e => e.Id_Usuario == usuario && e.ContextoRegistrado && e.EstadoEntrega == "accepted" && e.FechaEnvio >= since)
            .OrderByDescending(e => e.FechaEnvio).Select(e => new {
                campania = e.Campania.Titulo,
                e.FechaEnvio,
                e.HizoClick,
                e.FechaVulneracion,
                e.CapacitacionCompleta,
                idCategoria = e.IdCategoriaRegistro,
                e.FechaReporte,
                e.ReporteSinClickPrevio
            }).ToListAsync(ct);

        var n = evidencias.Count;
        var clics = evidencias.Count(e => e.HizoClick);

        var reportes = evidencias.Count(e => e.FechaReporte.HasValue);
        var reportesSinClickPrevio = evidencias.Count(e => e.FechaReporte.HasValue && e.ReporteSinClickPrevio);

        var score = n >= 3 ? Math.Round(100m * clics / n, 1) : (decimal?)null;
        var idsTemas = evidencias.Where(e => e.HizoClick).Select(e => e.idCategoria).Where(c => c.HasValue).Select(c => c!.Value).ToList();

        idsTemas.AddRange(await completadas.Where(i => i.Exitoso == false).Select(i => i.Id_Categoria).Distinct().ToListAsync(ct));

        var recomendaciones = await db.Set<ActividadEducativa>().AsNoTracking().Where(a => a.Habilitada && idsTemas.Contains(a.IdCategoria))
            .Select(a => new {
                a.Id,
                a.Titulo,
                tema = a.Categoria.Nombre
            }).ToListAsync(ct);

        return new {
            puntosEducativos = puntos,
            nivel = 1 + puntos / reglas.PuntosPorNivel,
            progresoNivel = puntos % reglas.PuntosPorNivel,
            puntosSiguienteNivel = reglas.PuntosPorNivel - puntos % reglas.PuntosPorNivel,

            metaSemanal = new {
                inicio,
                fin = inicio.AddDays(7),
                temasCompletados = semana,
                objetivo = objetivoSemanal,
                objetivoConfigurado = reglas.MetaSemanalTemas,
                cumplida = semana >= objetivoSemanal
            },

            actividadesAprobadas = total,
            temasPracticados = temas,
            semanasActivas = semanas,

            logros = logros.Select(l => new {
                l.Id_Insignia,
                l.Nombre,
                l.Descripcion,
                l.TipoCondicion,
                l.objetivo,
                l.obtenido,
                avance = Math.Min(l.objetivo, Valor(l.TipoCondicion, m))
            }),

            historial,
            enCurso,
            movimientos,
            recomendaciones,
            cyberScoreV2 = await cyberScore.CalcularAsync(usuario, ct),
            cyberScore = new {
                valor = score,
                nivel = score is null ? "Sin evaluación suficiente" : score >= 30 ? "Alto" : score >= 10 ? "Medio" : "Bajo",
                simulaciones = n,
                clics,
                reportes,
                reportesSinClickPrevio,
                tasaReporte = n > 0 ? Math.Round(100m * reportes / n, 1) : (decimal?)null,
                politicaReportes = "reportes-v1",
                desde = since,
                factores = evidencias,
                explicacion = "Porcentaje de clics observados en simulaciones aceptadas por el proveedor de los últimos 90 días; requiere al menos 3 simulaciones. Los reportes se reconocen como factor preventivo complementario con su propia tasa, sin borrar clics ni modificar esa proporción. No otorgan puntos educativos. Este indicador es descriptivo, no una probabilidad de ataque real."
            }
        };
    }
}
