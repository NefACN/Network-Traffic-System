using System.Globalization;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// CyberScore v3: 100 - riesgo, con aportes que decaen por antiguedad
public sealed class CyberScoreService(IApplicationDbContext db)
{
    public const string Version = "cyberscore-v3";
    public const string SinEvaluacion = "Sin evaluación suficiente";
    public static readonly string[] Niveles = ["Bajo", "Medio", "Alto", "Crítico"];

    public Task<PoliticaCyberScore> PoliticaAsync(CancellationToken ct = default) =>
        db.Set<PoliticaCyberScore>().AsNoTracking().SingleAsync(ct);

    public static string Formula => "Aporte = peso × factor de dificultad × 0,5^(días / vida media). Riesgo = Σ aportes × exposición del área (0–100). CyberScore = 100 − riesgo.";

    // factor que multiplica el peso de una caida segun la dificultad NIST
    public static decimal FactorCaida(PoliticaCyberScore p, string? dificultad) => dificultad switch {
        "poco_dificil" => p.FactorPocoDificil,
        "muy_dificil" => p.FactorMuyDificil,
        _ => 1m,
    };

    // factor minimo de un reporte: reportar nunca penaliza el CyberScore
    public const decimal FactorReporteMinimo = 0.1m;

    // reportar un mensaje dificil vale mas: usa 2 - factor de caida, con un minimo positivo
    public static decimal FactorReporte(PoliticaCyberScore p, string? dificultad) => Math.Max(FactorReporteMinimo, 2m - FactorCaida(p, dificultad));

    public static (decimal Factor, string Nivel) Exposicion(PoliticaCyberScore p, string? criticidad)
    {
        var nivel = (criticidad ?? "").Trim().ToLower(CultureInfo.InvariantCulture);
        return nivel switch {
            "baja" or "bajo" => (p.ExposicionBaja, "Baja"),
            "alta" or "alto" or "crítica" or "critica" => (p.ExposicionAlta, "Alta"),
            _ => (p.ExposicionMedia, "Media"),
        };
    }

    // nivel de riesgo a partir del CyberScore (mas alto = mejor)
    public static string Nivel(PoliticaCyberScore p, decimal cyberScore) =>
        cyberScore >= p.UmbralBajo ? "Bajo" : cyberScore >= p.UmbralMedio ? "Medio" : cyberScore >= p.UmbralAlto ? "Alto" : "Crítico";

    // compatibilidad con v2: nivel segun el riesgo (0-100, mas alto = peor)
    public static string Nivel(decimal riesgo) => Nivel(new PoliticaCyberScore(), 100m - riesgo);

    public sealed record Contribucion(
        DateTime Fecha,
        string Tipo,
        string Concepto,
        decimal Peso,
        decimal FactorDificultad,
        decimal Decaimiento,
        decimal Aporte,
        string? Dificultad)
    {
        // efecto sobre el CyberScore, negativo baja el puntaje
        public decimal Impacto => -Aporte;
    }

    public sealed record Resultado(
        int IdUsuario,
        bool Evaluable,
        decimal? CyberScore,
        decimal Riesgo,
        string Nivel,
        decimal Vulnerabilidad,
        decimal SumaAportes,
        decimal Exposicion,
        string NivelExposicion,
        string? Area,
        int Simulaciones,
        int Clics,
        int Credenciales,
        int Reportes,
        List<Contribucion> Contribuciones,
        List<int> TemasCaidas,
        DateTime Desde
        );

    // datos minimos para el calculo, cargados por lotes
    private sealed record Envio(
        int Usuario,
        string Campania,
        DateTime? FechaEnvio,
        bool HizoClick,
        DateTime? FechaVulneracion,
        bool IngresoCredenciales,
        DateTime? FechaIngresoCredenciales,
        DateTime? FechaReporte,
        bool CapacitacionCompleta,
        DateTime? FechaCapacitacion,
        string? Dificultad,
        int? IdCategoria
        );
    private sealed record Intento(
        int Usuario,
        int Id,
        int? IdActividad,
        string Titulo,
        DateTime Fecha,
        int DiasRepeticion
        );

    // actividad aprobada que puede recuperar CyberScore
    public sealed record IntentoAprobado(
        int Id,
        int? IdActividad,
        string Titulo,
        DateTime Fecha,
        int DiasRepeticion
        );

    // una actividad solo recupera CyberScore una vez por periodo de repeticion
    public static List<IntentoAprobado> IntentosComputables(IEnumerable<IntentoAprobado> intentos)
    {
        var ultimo = new Dictionary<string, DateTime>();
        var computables = new List<IntentoAprobado>();
        foreach (var i in intentos.OrderBy(i => i.Fecha).ThenBy(i => i.Id))
        {
            var clave = i.IdActividad is { } a ? $"a:{a}" : $"t:{i.Titulo}";

            if (ultimo.TryGetValue(clave, out var previo) && i.Fecha < previo.AddDays(Math.Max(1, i.DiasRepeticion)))
                continue;
            ultimo[clave] = i.Fecha;
            computables.Add(i);
        }
        return computables;
    }
    private sealed record Incidente(int Usuario, string Codigo, DateTime Fecha);

    public async Task<Resultado> ResultadoAsync(int usuario, CancellationToken ct = default) =>
        (await ResultadosAsync([usuario], ct)).SingleOrDefault()
            ?? throw new BusinessRuleException("Colaborador no encontrado.", BusinessError.NotFound);

    // calcula el CyberScore con consulta fija
    public async Task<List<Resultado>> ResultadosAsync(IReadOnlyCollection<int> usuarios, CancellationToken ct = default)
    {
        var p = await PoliticaAsync(ct);
        var ahora = DateTime.UtcNow;
        var desde = ahora.AddDays(-p.VentanaDias);

        var personas = await db.Users.AsNoTracking().Where(u => usuarios.Contains(u.Id))
            .Select(u => new {
                u.Id,
                area = u.AreaOperativa != null ? u.AreaOperativa.Nombre : null,
                criticidad = u.AreaOperativa != null ? u.AreaOperativa.Criticidad.Nivel : null
            }).ToDictionaryAsync(x => x.Id, ct);

        var envios = (await db.EnviosSimulacion.AsNoTracking()
            .Where(e => usuarios.Contains(e.Id_Usuario) && e.ContextoRegistrado && e.EstadoEntrega == "accepted" && e.FechaEnvio >= desde)
            .Select(e => new Envio(
                e.Id_Usuario,
                e.Campania.Titulo,
                e.FechaEnvio,
                e.HizoClick,
                e.FechaVulneracion,
                e.IngresoCredenciales,
                e.FechaIngresoCredenciales,
                e.FechaReporte,
                e.CapacitacionCompleta,
                e.FechaCapacitacion,
                e.DificultadNistRegistro,
                e.IdCategoriaRegistro)
            )
            .ToListAsync(ct)).ToLookup(e => e.Usuario);

        var intentos = (await db.IntentoCursoVoluntario.AsNoTracking()
            .Where(i => usuarios.Contains(i.Id_Usuario) && i.Exitoso == true && i.FechaCompletado >= desde)
            .Select(i => new Intento(
                i.Id_Usuario,
                i.Id_Intento,
                i.IdActividad,
                i.TituloRegistro ?? i.Categoria.Nombre,
                i.FechaCompletado!.Value,
                i.DiasRepeticionConfigurados)
            )
            .ToListAsync(ct)).ToLookup(i => i.Usuario);

        // reportar un mensaje real confirmado como malicioso tambien es conducta preventiva
        var incidentes = (await db.Set<IncidenteReportado>().AsNoTracking()
            .Where(i => usuarios.Contains(i.IdUsuario) && i.Clasificacion == "malicioso" && i.FechaCreacion >= desde)
            .Select(i => new Incidente(
                i.IdUsuario,
                i.Codigo,
                i.FechaCreacion)
            ).ToListAsync(ct)).ToLookup(i => i.Usuario);

        var microlecciones = (await db.Set<AsignacionMicroleccion>().AsNoTracking()
            .Where(a => usuarios.Contains(a.IdUsuario) && a.IdIntento != null).Select(a => a.IdIntento!.Value).ToListAsync(ct)).ToHashSet();

        return usuarios.Where(personas.ContainsKey).Select(u =>
        {
            var persona = personas[u];
            return Calcular(
                p, ahora, desde, u,
                persona.area,
                persona.criticidad,
                envios[u].ToList(),
                intentos[u].ToList(),
                incidentes[u].ToList(),
                microlecciones);
        }).ToList();
    }

    private static Resultado Calcular(
        PoliticaCyberScore p,
        DateTime ahora,
        DateTime desde,
        int usuario,
        string? area,
        string? criticidad,
        List<Envio> envios,
        List<Intento> intentos,
        List<Incidente> incidentes,
        HashSet<int> microlecciones
        )
    {
        var aportes = new List<Contribucion>();
        void Agregar(
            DateTime fecha,
            string tipo,
            string concepto,
            decimal peso,
            decimal factor,
            string? dificultad
            )
        {
            var decaimiento = Estadistica.Decaimiento((ahora - fecha).TotalDays, p.VidaMediaDias);

            aportes.Add(new Contribucion(
                fecha,
                tipo,
                concepto,
                peso,
                factor,
                Math.Round(decaimiento, 3),
                Math.Round(peso * factor * decaimiento, 2), dificultad)
                );
        }

        foreach (var e in envios)
        {
            var enviado = e.FechaEnvio!.Value;

            if (e.HizoClick)
                Agregar(
                    e.FechaVulneracion ?? enviado,
                    "clic",
                    $"Clic en «{e.Campania}»",
                    p.PesoClic,
                    FactorCaida(p, e.Dificultad),
                    e.Dificultad
                    );

            if (e.IngresoCredenciales)
                Agregar(
                    e.FechaIngresoCredenciales ?? enviado,
                    "credenciales",
                    $"Credenciales en página falsa de «{e.Campania}»",
                    p.PesoCredenciales,
                    FactorCaida(p, e.Dificultad),
                    e.Dificultad
                    );

            if (e.FechaReporte is { } reporte)
                Agregar(
                    reporte,
                    "reporte",
                    $"Reporte de «{e.Campania}»",
                    -p.BonoReporte,
                    FactorReporte(p, e.Dificultad),
                    e.Dificultad
                    );

            if (e.CapacitacionCompleta && e.FechaCapacitacion is { } capacitacion)
                Agregar(
                    capacitacion,
                    "cuestionario",
                    $"Capacitación tras «{e.Campania}»",
                    -p.BonoCuestionario,
                    1m,
                    null
                    );
        }

        // cada microleccion asignada cuenta una vez, las voluntarias una vez por periodo
        foreach (var i in intentos.Where(i => microlecciones.Contains(i.Id)))
            Agregar(i.Fecha, "microleccion", $"Microlección «{i.Titulo}»", -p.BonoMicroleccion, 1m, null);

        var voluntarios = intentos.Where(i => !microlecciones.Contains(i.Id)).Select(i => new IntentoAprobado(i.Id, i.IdActividad, i.Titulo, i.Fecha, i.DiasRepeticion));

        foreach (var i in IntentosComputables(voluntarios))
            Agregar(i.Fecha, "cuestionario", $"Actividad aprobada «{i.Titulo}»", -p.BonoCuestionario, 1m, null);

        foreach (var i in incidentes)
            Agregar(i.Fecha, "reporte_real", $"Reporte real confirmado {i.Codigo}", -p.BonoReporte, 1m, null);

        var suma = aportes.Sum(a => a.Aporte);
        var vulnerabilidad = Math.Clamp(Math.Round(suma, 1), 0m, 100m);
        var (exposicion, nivelExposicion) = Exposicion(p, criticidad);
        var riesgo = Math.Clamp(Math.Round(vulnerabilidad * exposicion, 1), 0m, 100m);
        var evaluable = envios.Count >= p.MinimoSimulaciones;
        var score = 100m - riesgo;
        var temas = envios.Where(e => (e.HizoClick || e.IngresoCredenciales) && e.IdCategoria.HasValue).Select(e => e.IdCategoria!.Value).Distinct().ToList();

        return new Resultado(
            usuario,
            evaluable,
            evaluable ? score : null,
            riesgo,
            evaluable ? Nivel(p, score) : SinEvaluacion,
            vulnerabilidad,
            Math.Round(suma, 2),
            exposicion,
            nivelExposicion,
            area,
            envios.Count,
            envios.Count(e => e.HizoClick),
            envios.Count(e => e.IngresoCredenciales),
            envios.Count(e => e.FechaReporte.HasValue),
            aportes.OrderByDescending(a => a.Fecha).ToList(),
            temas,
            desde
            );
    }

    // vista personal
    public async Task<object> CalcularAsync(int usuario, CancellationToken ct = default)
    {
        var p = await PoliticaAsync(ct);
        var r = await ResultadoAsync(usuario, ct);
        return new {
            version = Version,
            revision = p.Revision,
            evaluable = r.Evaluable,
            valor = r.CyberScore,
            cyberScore = r.CyberScore,
            riesgo = r.Riesgo,
            nivel = r.Nivel,
            vulnerabilidad = r.Vulnerabilidad,
            sumaAportes = r.SumaAportes,
            exposicion = r.Exposicion,
            nivelExposicion = r.NivelExposicion,
            area = r.Area,
            simulaciones = r.Simulaciones,
            clics = r.Clics,
            credenciales = r.Credenciales,
            reportes = r.Reportes,
            minimoSimulaciones = p.MinimoSimulaciones,
            desde = r.Desde,
            umbrales = new { bajo = p.UmbralBajo, medio = p.UmbralMedio, alto = p.UmbralAlto },
            contribuciones = r.Contribuciones.Select(
                c => new {
                    c.Fecha,
                    c.Tipo,
                    c.Concepto,
                    c.Peso,
                    c.FactorDificultad,
                    c.Decaimiento,
                    c.Aporte,
                    c.Impacto,
                    c.Dificultad
                }),
            pesos = new {
                p.PesoClic,
                p.PesoCredenciales,
                p.BonoReporte,
                p.BonoCuestionario,
                p.BonoMicroleccion,
                p.VidaMediaDias,
                p.VentanaDias,
                p.FactorPocoDificil,
                p.FactorMuyDificil,
                p.ExposicionBaja,
                p.ExposicionMedia,
                p.ExposicionAlta
            },

            formula = Formula,

            explicacion = $"Tu CyberScore parte de 100 y considera los últimos {p.VentanaDias} días. Hacer clic o entregar credenciales en una simulación lo baja; "
                + "reportar el mensaje, completar capacitaciones y microlecciones lo recupera. Cada hecho pierde la mitad de su efecto cada "
                + $"{p.VidaMediaDias} días. Caer en un mensaje fácil de detectar descuenta más, y reportar uno difícil suma más. La exposición de tu área ({r.NivelExposicion}) "
                + "multiplica el efecto. Es un indicador para orientar tu aprendizaje: no es una sanción ni una probabilidad de ataque."
        };
    }

    public async Task GuardarPoliticaAsync(PoliticaCyberScoreRequest r, CancellationToken ct = default)
    {
        if (r.FactorMuyDificil > 1m || r.FactorPocoDificil < 1m)
            throw new BusinessRuleException("El factor para mensajes poco difíciles debe ser ≥ 1 y el de muy difíciles ≤ 1.");

        // con 2 - factor, un factor >= 2 haria que reportar un mensaje facil penalice
        if (r.FactorPocoDificil > 1.9m)
            throw new BusinessRuleException("El factor para mensajes poco difíciles debe ser como máximo 1,9 para que reportar siempre recupere CyberScore.");

        if (r.ExposicionBaja > r.ExposicionMedia || r.ExposicionMedia > r.ExposicionAlta)
            throw new BusinessRuleException("La exposición debe crecer de baja a media y de media a alta.");

        if (r.VidaMediaDias > r.VentanaDias)
            throw new BusinessRuleException("La vida media no puede superar la ventana de cálculo.");

        if (!(r.UmbralBajo > r.UmbralMedio && r.UmbralMedio > r.UmbralAlto && r.UmbralAlto > 0m))
            throw new BusinessRuleException("Los umbrales deben decrecer: Bajo > Medio > Alto > 0.");

        var p = await db.Set<PoliticaCyberScore>().SingleAsync(ct);

        p.PesoClic = r.PesoClic;
        p.PesoCredenciales = r.PesoCredenciales;
        p.BonoReporte = r.BonoReporte;
        p.BonoCuestionario = r.BonoCuestionario;
        p.BonoMicroleccion = r.BonoMicroleccion;
        p.VidaMediaDias = r.VidaMediaDias;
        p.VentanaDias = r.VentanaDias;
        p.MinimoSimulaciones = r.MinimoSimulaciones;
        p.FactorPocoDificil = r.FactorPocoDificil;
        p.FactorMuyDificil = r.FactorMuyDificil;
        p.ExposicionBaja = r.ExposicionBaja;
        p.ExposicionMedia = r.ExposicionMedia;
        p.ExposicionAlta = r.ExposicionAlta;
        p.UmbralBajo = r.UmbralBajo;
        p.UmbralMedio = r.UmbralMedio;
        p.UmbralAlto = r.UmbralAlto;
        p.MaximoPlanCritico = r.MaximoPlanCritico;
        p.Revision++;
        p.ActualizadaEn = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
