using System.Data;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class ResultadosPreventivosService(IApplicationDbContext db)
{
    public async Task<object> CatalogosAsync(CancellationToken ct) => new {
        campanias = await db.CampaniasSimulacion.AsNoTracking().OrderByDescending(c => c.Id_Campania)
            .Select(c => new {
                idCampania = c.Id_Campania,
                c.Titulo,
                c.TipoEvaluacion,
                estado = c.Estado.NombreEstado,
                idEstado = c.Id_Estado,
                c.FechaInicioProgramada,
                c.FechaFinProgramada,
                c.FechaFinalizacion,
                participantes = c.Envios.Select(e => e.Id_Usuario).Distinct().Count(),
                c.VersionMedicionRegistro,
                dificultad = c.NivelDificultadRegistro,
                tema = db.CategoriasPregunta
                    .Where(t => t.IdCategoria == c.IdCategoriaRegistro)
                    .Select(t => t.Nombre)
                    .FirstOrDefault()
            }).ToListAsync(ct),
        sucursales = await db.Sucursales.AsNoTracking()
            .OrderBy(s => s.Nombre)
            .Select(s => new { id = s.Id_Sucursal, s.Nombre, s.Activo })
            .ToListAsync(ct),
        areas = await db.AreasOperativas.AsNoTracking()
            .OrderBy(a => a.Nombre)
            .Select(a => new { id = a.Id_Area, a.Nombre, a.Activo })
            .ToListAsync(ct)
    };

    private static void Validar(ResultadosFiltro f)
    {
        if (f.IdSucursal is < 1 || f.IdArea is < 1 || f.Desde > f.Hasta)
            throw new BusinessRuleException("Revisa la sucursal, el área y el intervalo de fechas.");
    }

    private static int Desplazamiento(int pagina, int tamanio)
    {
        if (tamanio is < 1 or > 200 || pagina < 1 || pagina > int.MaxValue / tamanio)
            throw new BusinessRuleException("La paginación solicitada está fuera del rango permitido.");

        return (pagina - 1) * tamanio;
    }

    private IQueryable<EnvioSimulacion> Envios(int id, ResultadosFiltro f)
    {
        Validar(f);
        var q = db.EnviosSimulacion.AsNoTracking().Where(e => e.Id_Campania == id);

        if (f.IdSucursal.HasValue)
            q = q.Where(e => e.IdSucursalRegistro == f.IdSucursal);
        if (f.IdArea.HasValue)
            q = q.Where(e => e.IdAreaRegistro == f.IdArea);
        if (f.Desde.HasValue)
        {
            var inicio = f.Desde.Value.UtcDateTime;
            q = q.Where(e => e.FechaEnvio >= inicio);
        }
        if (f.Hasta.HasValue)
        {
            var fin = f.Hasta.Value.UtcDateTime;
            q = q.Where(e => e.FechaEnvio <= fin);
        }

        return q;
    }

    private async Task<CampaniaSimulacion> CampaniaAsync(int id, CancellationToken ct) =>
        await db.CampaniasSimulacion.AsNoTracking()
            .Include(c => c.Estado)
            .SingleOrDefaultAsync(c => c.Id_Campania == id, ct)
            ?? throw new BusinessRuleException("La campaña no existe.", BusinessError.NotFound);

    private IQueryable<ParticipanteResultadoDto> Proyectar(IQueryable<EnvioSimulacion> q) => q.Select(e => new ParticipanteResultadoDto(
        e.Id_Envio, e.Id_Usuario, e.Usuario.Nombre + " " + e.Usuario.Apellido, e.Usuario.Email,
        e.IdSucursalRegistro, db.Sucursales.Where(s => s.Id_Sucursal == e.IdSucursalRegistro).Select(s => s.Nombre).FirstOrDefault(),
        e.IdAreaRegistro, db.AreasOperativas.Where(a => a.Id_Area == e.IdAreaRegistro).Select(a => a.Nombre).FirstOrDefault(),
        e.EstadoEntrega, e.ContextoRegistrado, e.FechaEnvio, e.FueAbierto, e.FechaApertura, e.HizoClick, e.FechaVulneracion,
        e.FechaReporte, e.ReporteSinClickPrevio, e.CapacitacionCompleta, e.FechaCapacitacion, e.PuntosRecuperados, e.IdCategoriaRegistro,
        e.IngresoCredenciales, e.FechaIngresoCredenciales, e.DificultadNistRegistro, e.AlineacionRegistro));

    private static bool Evaluable(ParticipanteResultadoDto e) => e.EstadoEntrega == "accepted" && e.ContextoRegistrado
        && e.IdSucursal.HasValue && e.IdArea.HasValue && e.IdCategoria.HasValue && e.FechaEnvio.HasValue;

    private static bool FechasCompletas(ParticipanteResultadoDto e) => (!e.FueAbierto || e.FechaApertura.HasValue)
        && (!e.HizoClick || e.FechaVulneracion.HasValue) && (!e.IngresoCredenciales || e.FechaIngresoCredenciales.HasValue);

    private static decimal? Tasa(int valor, int total) => total > 0 ? Math.Round(100m * valor / total, 2) : null;

    private static IndicadoresPareadosDto Medir(IReadOnlyCollection<ParticipanteResultadoDto> e) => new(
        e.Count,
        e.Count(x => x.FueAbierto),
        e.Count(x => x.HizoClick),
        e.Count(x => x.FechaReporte.HasValue),
        e.Count(x => x.FechaReporte.HasValue && x.ReporteSinClickPrevio),
        Tasa(e.Count(x => x.FueAbierto), e.Count),
        Tasa(e.Count(x => x.HizoClick), e.Count),
        Tasa(e.Count(x => x.FechaReporte.HasValue), e.Count),
        e.Count(x => x.IngresoCredenciales),
        Tasa(e.Count(x => x.IngresoCredenciales), e.Count));

    // mediana en minutos de duraciones no negativas, null si no hay datos
    internal static decimal? MedianaMinutos(IEnumerable<TimeSpan> duraciones)
    {
        var v = duraciones.Where(d => d >= TimeSpan.Zero).Select(d => d.TotalMinutes).Order().ToList();
        if (v.Count == 0)
            return null;

        var m = v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        return Math.Round((decimal)m, 1);
    }

    public async Task<object> ResumenAsync(int id, ResultadosFiltro f, CancellationToken ct)
    {
        Validar(f);
        var c = await CampaniaAsync(id, ct);
        var q = Envios(id, f);

        var total = await q.GroupBy(e => 1).Select(g => new {
            envios = g.Count(),
            participantes = g.Select(e => e.Id_Usuario).Distinct().Count(),
            aceptados = g.Count(e => e.EstadoEntrega == "accepted"),
            fallidos = g.Count(e => e.EstadoEntrega == "failed"),
            pendientes = g.Count(e => e.EstadoEntrega == "pending"),
            cancelados = g.Count(e => e.EstadoEntrega == "canceled"),
            historicos = g.Count(e => e.EstadoEntrega == "legacy"),
            aperturasRegistradas = g.Count(e => e.FueAbierto),
            clicsRegistrados = g.Count(e => e.HizoClick),
            reportesRegistrados = g.Count(e => e.FechaReporte != null),
            credencialesRegistradas = g.Count(e => e.IngresoCredenciales),
            capacitacionesRegistradas = g.Count(e => e.CapacitacionCompleta),
            sinContexto = g.Count(e => !e.ContextoRegistrado
                || e.IdSucursalRegistro == null
                || e.IdAreaRegistro == null
                || e.IdCategoriaRegistro == null
                || e.FechaEnvio == null),
            interaccionesSinFecha = g.Count(e => (e.FueAbierto && e.FechaApertura == null)
                || (e.HizoClick && e.FechaVulneracion == null)
                || (e.CapacitacionCompleta && e.FechaCapacitacion == null)
                || (e.IngresoCredenciales && e.FechaIngresoCredenciales == null))
        }).SingleOrDefaultAsync(ct);

        var validos = q.Where(e => e.EstadoEntrega == "accepted"
            && e.ContextoRegistrado
            && e.IdSucursalRegistro != null
            && e.IdAreaRegistro != null
            && e.IdCategoriaRegistro != null
            && e.FechaEnvio != null);
        var r = await validos.GroupBy(e => 1).Select(g => new {
            envios = g.Count(),
            aperturas = g.Count(e => e.FueAbierto),
            clics = g.Count(e => e.HizoClick),
            reportes = g.Count(e => e.FechaReporte != null),
            reportesSinClickPrevio = g.Count(e => e.FechaReporte != null && e.ReporteSinClickPrevio),
            capacitaciones = g.Count(e => e.CapacitacionCompleta),
            credenciales = g.Count(e => e.IngresoCredenciales)
        }).SingleOrDefaultAsync(ct);

        // tiempos y desglose por dificultad NIST, calculados sobre los envios evaluables
        var detalle = await validos.Select(e => new {
            e.FechaEnvio,
            e.FechaVulneracion,
            e.FechaReporte,
            e.FechaIngresoCredenciales,
            e.HizoClick,
            e.IngresoCredenciales,
            e.DificultadNistRegistro
        }).ToListAsync(ct);
        var tiempos = new {
            medianaMinutosHastaClic = MedianaMinutos(detalle
                .Where(e => e.HizoClick && e.FechaVulneracion != null)
                .Select(e => e.FechaVulneracion!.Value - e.FechaEnvio!.Value)),
            medianaMinutosHastaReporte = MedianaMinutos(detalle
                .Where(e => e.FechaReporte != null)
                .Select(e => e.FechaReporte!.Value - e.FechaEnvio!.Value)),
            medianaMinutosHastaCredenciales = MedianaMinutos(detalle
                .Where(e => e.IngresoCredenciales && e.FechaIngresoCredenciales != null)
                .Select(e => e.FechaIngresoCredenciales!.Value - e.FechaEnvio!.Value)),
        };
        var porDificultad = detalle
            .GroupBy(e => e.DificultadNistRegistro)
            .OrderByDescending(g => PhishScale.Rango(g.Key) ?? 0)
            .Select(g => new {
                dificultad = g.Key,
                etiqueta = PhishScale.Etiqueta(g.Key),
                envios = g.Count(),
                clics = g.Count(e => e.HizoClick),
                credenciales = g.Count(e => e.IngresoCredenciales),
                reportes = g.Count(e => e.FechaReporte != null),
                tasaClic = Tasa(g.Count(e => e.HizoClick), g.Count()),
                tasaCredenciales = Tasa(g.Count(e => e.IngresoCredenciales), g.Count()),
                tasaReporte = Tasa(g.Count(e => e.FechaReporte != null), g.Count())
            }).ToList();

        return new {
            campania = new {
                idCampania = id,
                c.Titulo,
                c.Objetivo,
                c.TipoEvaluacion,
                estado = c.Estado.NombreEstado,
                c.FechaInicioProgramada,
                c.FechaFinProgramada,
                c.FechaFinalizacion
            },
            generadoEn = DateTime.UtcNow,
            parcial = c.Id_Estado == 2,
            pendiente = c.Id_Estado == 1,
            filtros = f,
            sinDatos = total is null,
            sinInteracciones = total is null
                || total.aperturasRegistradas
                    + total.clicsRegistrados
                    + total.reportesRegistrados
                    + total.capacitacionesRegistradas
                    + total.credencialesRegistradas == 0,
            totales = total,
            indicadores = new {
                evaluables = r?.envios ?? 0,
                aperturas = r?.aperturas ?? 0,
                clics = r?.clics ?? 0,
                reportes = r?.reportes ?? 0,
                reportesSinClickPrevio = r?.reportesSinClickPrevio ?? 0,
                capacitaciones = r?.capacitaciones ?? 0,
                tasaApertura = Tasa(r?.aperturas ?? 0, r?.envios ?? 0),
                tasaClic = Tasa(r?.clics ?? 0, r?.envios ?? 0),
                tasaReporte = Tasa(r?.reportes ?? 0, r?.envios ?? 0),
                credenciales = r?.credenciales ?? 0,
                tasaCredenciales = Tasa(r?.credenciales ?? 0, r?.envios ?? 0),
                reportesPorClic = r is { clics: > 0 } ? Math.Round((decimal)r.reportes / r.clics, 2) : (decimal?)null
            },
            tiempos,
            porDificultad,
            paginaSenuelo = c.PaginaSenueloRegistro,
            nota = "Las fechas filtran envíos; se consultan las interacciones de esos envíos. Las tasas utilizan aceptación del proveedor y contexto histórico verificable. Los registros anteriores o incompletos se conservan por separado. La aceptación no confirma entrega y una apertura puede proceder de mecanismos automáticos."
        };
    }

    public async Task<object> ParticipantesAsync(int id, ResultadosFiltro f, int pagina, int tamanio, CancellationToken ct)
    {
        var offset = Desplazamiento(pagina, tamanio);
        await CampaniaAsync(id, ct);
        var q = Envios(id, f);

        return new {
            pagina,
            tamanio,
            total = await q.CountAsync(ct),
            items = await Proyectar(q
                .OrderBy(e => e.Usuario.Nombre)
                .ThenBy(e => e.Usuario.Apellido)
                .ThenBy(e => e.Id_Envio)
                .Skip(offset)
                .Take(tamanio)).ToListAsync(ct)
        };
    }

    public async Task<ParticipanteResultadoDto> IndividualAsync(int id, int envio, CancellationToken ct) =>
        await Proyectar(db.EnviosSimulacion.AsNoTracking().Where(e => e.Id_Campania == id && e.Id_Envio == envio))
            .SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("El participante no tiene un envío en esta campaña.", BusinessError.NotFound);

    public async Task<object> InteraccionesAsync(int id, ResultadosFiltro f, string? tipo, int pagina, int tamanio, CancellationToken ct)
    {
        var offset = Desplazamiento(pagina, tamanio);
        if (!string.IsNullOrEmpty(tipo) && tipo is not ("apertura" or "clic" or "credenciales" or "reporte" or "capacitacion"))
            throw new BusinessRuleException("Selecciona un tipo de interacción válido.");

        await CampaniaAsync(id, ct);
        var e = Envios(id, f);
        var eventos = e
            .Where(x => x.FueAbierto && x.FechaApertura != null)
            .Select(x => new {
                idEnvio = x.Id_Envio,
                idUsuario = x.Id_Usuario,
                nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido,
                correo = x.Usuario.Email,
                tipo = "apertura",
                fecha = x.FechaApertura
            })
            .Concat(e
                .Where(x => x.HizoClick && x.FechaVulneracion != null)
                .Select(x => new {
                    idEnvio = x.Id_Envio,
                    idUsuario = x.Id_Usuario,
                    nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido,
                    correo = x.Usuario.Email,
                    tipo = "clic",
                    fecha = x.FechaVulneracion
                }))
            .Concat(e
                .Where(x => x.IngresoCredenciales && x.FechaIngresoCredenciales != null)
                .Select(x => new {
                    idEnvio = x.Id_Envio,
                    idUsuario = x.Id_Usuario,
                    nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido,
                    correo = x.Usuario.Email,
                    tipo = "credenciales",
                    fecha = x.FechaIngresoCredenciales
                }))
            .Concat(e
                .Where(x => x.FechaReporte != null)
                .Select(x => new {
                    idEnvio = x.Id_Envio,
                    idUsuario = x.Id_Usuario,
                    nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido,
                    correo = x.Usuario.Email,
                    tipo = "reporte",
                    fecha = x.FechaReporte
                }))
            .Concat(e
                .Where(x => x.CapacitacionCompleta && x.FechaCapacitacion != null)
                .Select(x => new {
                    idEnvio = x.Id_Envio,
                    idUsuario = x.Id_Usuario,
                    nombre = x.Usuario.Nombre + " " + x.Usuario.Apellido,
                    correo = x.Usuario.Email,
                    tipo = "capacitacion",
                    fecha = x.FechaCapacitacion
                }));
        if (!string.IsNullOrEmpty(tipo))
            eventos = eventos.Where(x => x.tipo == tipo);

        return new {
            pagina,
            tamanio,
            total = await eventos.CountAsync(ct),
            items = await eventos
                .OrderByDescending(x => x.fecha)
                .ThenBy(x => x.idEnvio)
                .ThenBy(x => x.tipo)
                .Skip(offset)
                .Take(tamanio)
                .ToListAsync(ct)
        };
    }

    public async Task<object> CompararAsync(int inicial, int posterior, ResultadosFiltro f, CancellationToken ct)
    {
        Validar(f);
        if (inicial == posterior)
            throw new BusinessRuleException("Selecciona dos campañas diferentes.");

        // instantanea consistente para no mezclar datos si algo cambia durante la lectura
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var a = await CampaniaAsync(inicial, ct);
        var b = await CampaniaAsync(posterior, ct);
        var aa = await Proyectar(Envios(inicial, f)).ToListAsync(ct);
        var bb = await Proyectar(Envios(posterior, f)).ToListAsync(ct);
        var limitaciones = new List<string>();
        var advertencias = new List<string>();

        if (a.TipoEvaluacion != "inicial" || b.TipoEvaluacion != "posterior")
            limitaciones.Add("Selecciona una evaluación inicial y una posterior, en ese orden.");
        if (a.Id_Estado is 1 or 2
            || b.Id_Estado is 1 or 2
            || !a.FechaFinalizacion.HasValue
            || !b.FechaFinalizacion.HasValue)
            limitaciones.Add("Las dos campañas deben estar finalizadas y tener su fecha de cierre registrada.");
        if (!CampaniasService.VersionesComparables.Contains(a.VersionMedicionRegistro ?? "")
            || b.VersionMedicionRegistro != a.VersionMedicionRegistro)
            limitaciones.Add("Falta una versión verificable y compatible de los indicadores. Los datos históricos no se reclasifican automáticamente.");
        if (!a.IdCategoriaRegistro.HasValue || a.IdCategoriaRegistro != b.IdCategoriaRegistro ||
            !a.NivelDificultadRegistro.HasValue || a.NivelDificultadRegistro != b.NivelDificultadRegistro)
            limitaciones.Add("El tema y la dificultad capturados al iniciar deben coincidir.");

        var mensajesDistintos = string.IsNullOrWhiteSpace(a.CuerpoHtmlRegistro) || string.IsNullOrWhiteSpace(b.CuerpoHtmlRegistro) ||
            a.CuerpoHtmlRegistro != b.CuerpoHtmlRegistro || a.AsuntoRegistro != b.AsuntoRegistro;
        var inicioPosterior = await db.EnviosSimulacion
            .Where(e => e.Id_Campania == posterior)
            .MinAsync(e => e.FechaEnvio, ct);
        if (!inicioPosterior.HasValue || !a.FechaFinalizacion.HasValue || inicioPosterior < a.FechaFinalizacion)
            limitaciones.Add("La evaluación posterior debe comenzar después del cierre de la inicial.");

        bool Elegible(ParticipanteResultadoDto e, CampaniaSimulacion c) =>
            Evaluable(e) && FechasCompletas(e) && e.IdCategoria == c.IdCategoriaRegistro;
        var validosA = aa.Where(e => Elegible(e, a)).ToList();
        var validosB = bb.Where(e => Elegible(e, b)).ToList();
        var ids = validosA.Select(e => e.IdUsuario).Intersect(validosB.Select(e => e.IdUsuario)).ToHashSet();
        if (ids.Count == 0)
            limitaciones.Add("No hay participantes comunes con resultados válidos en ambas evaluaciones.");

        var paresA = validosA.Where(e => ids.Contains(e.IdUsuario)).OrderBy(e => e.IdUsuario).ToList();
        var paresB = validosB.Where(e => ids.Contains(e.IdUsuario)).OrderBy(e => e.IdUsuario).ToList();

        // NIST Phish Scale: cada persona debe enfrentar la misma dificultad en ambas evaluaciones
        var nistEquivalente = false;
        if (a.VersionMedicionRegistro == CampaniasService.VersionMedicion && ids.Count > 0)
        {
            var dificultadB = validosB
                .Where(e => ids.Contains(e.IdUsuario))
                .ToDictionary(e => e.IdUsuario, e => e.DificultadNist);
            var distintas = validosA.Count(e => ids.Contains(e.IdUsuario) && e.DificultadNist != dificultadB[e.IdUsuario]);
            if (distintas > 0)
                limitaciones.Add($"La dificultad NIST Phish Scale capturada difiere para {distintas} participante(s) comunes (cambio de señales, alineación o área).");

            nistEquivalente = distintas == 0 && validosA.Where(e => ids.Contains(e.IdUsuario)).All(e => e.DificultadNist != null);
        }

        // con la misma dificultad NIST se admiten mensajes distintos, si no se exige el mismo
        if (mensajesDistintos)
        {
            if (nistEquivalente)
                advertencias.Add("Los mensajes difieren; la comparación se apoya en el mismo tema y la misma dificultad NIST Phish Scale para cada participante.");
            else
                limitaciones.Add("Los mensajes originales no son verificables o difieren en contenido/asunto; se omite la variación directa. Califica ambas plantillas con NIST Phish Scale para comparar mensajes distintos de igual dificultad.");
        }

        if (ids.Count is > 0 and < 5)
            advertencias.Add("El grupo pareado tiene menos de cinco personas. Los resultados son descriptivos y no permiten inferir eficacia.");

        var duracionA = a.FechaFinProgramada - a.FechaInicioProgramada;
        var duracionB = b.FechaFinProgramada - b.FechaInicioProgramada;
        if (duracionA is null
            || duracionB is null
            || duracionA != duracionB
            || a.FinalizacionAnticipada
            || b.FinalizacionAnticipada)
            advertencias.Add("Las ventanas de observación no son equivalentes o hubo un cierre anticipado. Interpreta las diferencias con esa limitación.");
        if (validosA.Count != ids.Count || validosB.Count != ids.Count)
            advertencias.Add("Los grupos originales difieren; las tasas pareadas excluyen a quienes no tienen una evaluación válida en ambos grupos.");
        if (aa.Count != validosA.Count || bb.Count != validosB.Count)
            advertencias.Add("Se excluyeron envíos sin aceptación o con contexto/fechas incompletos.");

        var comparable = limitaciones.Count == 0;
        var metricasA = comparable ? Medir(paresA) : null;
        var metricasB = comparable ? Medir(paresB) : null;
        var porUsuarioB = paresB.ToDictionary(e => e.IdUsuario);
        var pares = paresA.Select(x =>
        {
            var y = porUsuarioB[x.IdUsuario];
            return new {
                x.IdUsuario,
                x.Nombre,
                x.Correo,
                inicial = new {
                    x.FueAbierto,
                    x.HizoClick,
                    x.FechaReporte,
                    x.ReporteSinClickPrevio,
                    x.IngresoCredenciales,
                    x.DificultadNist
                },
                posterior = new {
                    y.FueAbierto,
                    y.HizoClick,
                    y.FechaReporte,
                    y.ReporteSinClickPrevio,
                    y.IngresoCredenciales,
                    y.DificultadNist
                }
            };
        }).ToList();

        // prueba de McNemar exacta sobre los mismos participantes (antes/despues)
        object Prueba(string indicador, Func<ParticipanteResultadoDto, bool> ocurrio, bool menorEsMejor)
        {
            int ambos = 0, soloInicial = 0, soloPosterior = 0, ninguno = 0;
            foreach (var x in paresA)
            {
                bool i = ocurrio(x), f = ocurrio(porUsuarioB[x.IdUsuario]);
                if (i && f)
                    ambos++;
                else if (i)
                    soloInicial++;
                else if (f)
                    soloPosterior++;
                else
                    ninguno++;
            }

            var discordantes = soloInicial + soloPosterior;
            var p = Estadistica.McNemarExacto(soloInicial, soloPosterior);
            var mejora = menorEsMejor ? soloPosterior < soloInicial : soloPosterior > soloInicial;
            var interpretacion = discordantes == 0 ? "Nadie cambió de resultado entre ambas evaluaciones."
                : p < 0.05 ? $"Cambio estadísticamente significativo (p < 0,05) hacia {(mejora ? "una mejora" : "un empeoramiento")}."
                : "La diferencia no es estadísticamente significativa (α = 0,05); puede deberse al azar.";

            return new {
                indicador,
                ambos,
                soloInicial,
                soloPosterior,
                ninguno,
                discordantes,
                p = Math.Round(p, 4),
                significativo = p < 0.05,
                mejora,
                interpretacion
            };
        }

        var pruebas = new List<object>();
        if (comparable)
        {
            pruebas.Add(Prueba("Clic", x => x.HizoClick, true));
            if (a.PaginaSenueloRegistro == "credenciales" && b.PaginaSenueloRegistro == "credenciales")
                pruebas.Add(Prueba("Credenciales", x => x.IngresoCredenciales, true));
            pruebas.Add(Prueba("Reporte", x => x.FechaReporte.HasValue, false));
        }

        object Info(CampaniaSimulacion c) => new {
            idCampania = c.Id_Campania,
            c.Titulo,
            c.TipoEvaluacion,
            c.FechaInicioProgramada,
            c.FechaFinProgramada,
            c.FechaFinalizacion,
            c.VersionMedicionRegistro,
            c.IdCategoriaRegistro,
            dificultad = c.NivelDificultadRegistro
        };

        var result = new {
            comparable,
            condicionesEquivalentes = comparable && advertencias.Count == 0,
            limitaciones,
            advertencias,
            generadoEn = DateTime.UtcNow,
            filtros = f,
            inicial = Info(a),
            posterior = Info(b),
            participantesComunes = ids.Count,
            indicadoresIniciales = metricasA,
            indicadoresPosteriores = metricasB,
            pruebas,
            metodoPrueba = "McNemar exacto (binomial bilateral) sobre participantes pareados; α = 0,05",
            variaciones = comparable ? new {
                clicsPuntosPorcentuales = metricasB!.TasaClic - metricasA!.TasaClic,
                reportesPuntosPorcentuales = metricasB.TasaReporte - metricasA.TasaReporte,
                credencialesPuntosPorcentuales = metricasB.TasaCredenciales - metricasA.TasaCredenciales,
                aperturasPuntosPorcentuales = metricasB.TasaApertura - metricasA.TasaApertura
            } : null,
            participantes = comparable ? (object)pares : Array.Empty<object>(),
            excluidosInicial = validosA
                .Where(e => !ids.Contains(e.IdUsuario))
                .Select(e => new { e.IdUsuario, e.Nombre, e.Correo, motivo = "Sin evaluación posterior válida dentro de los filtros" })
                .ToList(),
            excluidosPosterior = validosB
                .Where(e => !ids.Contains(e.IdUsuario))
                .Select(e => new { e.IdUsuario, e.Nombre, e.Correo, motivo = "Sin evaluación inicial válida dentro de los filtros" })
                .ToList(),
            registrosExcluidos = aa
                .Where(e => !Elegible(e, a))
                .Select(e => new {
                    evaluacion = "inicial",
                    e.IdEnvio,
                    e.IdUsuario,
                    e.Nombre,
                    motivo = "Envío no aceptado o trazabilidad incompleta/incompatible"
                })
                .Concat(bb
                    .Where(e => !Elegible(e, b))
                    .Select(e => new {
                        evaluacion = "posterior",
                        e.IdEnvio,
                        e.IdUsuario,
                        e.Nombre,
                        motivo = "Envío no aceptado o trazabilidad incompleta/incompatible"
                    }))
                .ToList(),
            nota = "Comparación descriptiva pareada de los mismos participantes. No demuestra causalidad ni eficacia de una capacitación. Las aperturas son informativas y pueden incluir automatizaciones; un clic no confirma un compromiso real."
        };

        await tx.CommitAsync(ct);
        return result;
    }
}
