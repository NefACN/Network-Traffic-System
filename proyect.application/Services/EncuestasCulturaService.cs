using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed record PreguntaEncuesta(string Id, string Texto, string Dimension);
public sealed record EncuestaRequest(
    string Titulo,
    string Periodo,
    DateTime FechaInicio,
    DateTime FechaFin,
    List<PreguntaEncuesta>? Preguntas);

// encuesta de cultura (Likert 1-5), participacion y respuesta se guardan por separado
public sealed class EncuestasCulturaService(IApplicationDbContext db, IAuditoriaService auditoria, GobernanzaService gobernanza)
{
    public static readonly IReadOnlyDictionary<string, string> Dimensiones = new Dictionary<string, string> {
        ["actitud"] = "Actitud hacia la seguridad",
        ["conocimiento"] = "Conocimiento percibido",
        ["comportamiento"] = "Comportamiento",
        ["reporte"] = "Confianza para reportar",
        ["apoyo"] = "Apoyo de la organización",
    };

    public static readonly IReadOnlyList<PreguntaEncuesta> PreguntasBase =
    [
        new("a1", "La seguridad de la información es parte de mi trabajo, no solo del área de sistemas.", "actitud"),
        new("a2", "Vale la pena tomarse unos segundos para verificar un mensaje antes de actuar.", "actitud"),
        new("c1", "Sé reconocer las señales de un mensaje de phishing.", "conocimiento"),
        new("c2", "Sé qué hacer si escribí mi contraseña en un sitio sospechoso.", "conocimiento"),
        new("b1", "Verifico el remitente y el enlace antes de abrir adjuntos o hacer clic.", "comportamiento"),
        new("b2", "Uso contraseñas distintas y la verificación en dos pasos en mis cuentas de trabajo.", "comportamiento"),
        new("r1", "Me siento cómodo reportando un mensaje sospechoso, aunque me haya equivocado.", "reporte"),
        new("r2", "Sé cómo y dónde reportar un incidente de seguridad.", "reporte"),
        new("p1", "Mi jefatura considera importante la seguridad de la información.", "apoyo"),
        new("p2", "Las capacitaciones de seguridad me resultan útiles para mi trabajo diario.", "apoyo"),
    ];

    // encuestas paginadas, de la mas reciente a la mas antigua
    public async Task<object> ListarAsync(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.Set<EncuestaCultura>().AsNoTracking()
            .OrderByDescending(e => e.FechaInicio)
            .ThenByDescending(e => e.Id)
            .Select(e => new {
                e.Id,
                e.Titulo,
                e.Periodo,
                e.FechaInicio,
                e.FechaFin,
                e.Estado,
                respuestas = db.Set<RespuestaEncuesta>().Count(r => r.IdEncuesta == e.Id),
            }).PaginarAsync(pagina, tamanio, ct);

    public async Task<int> CrearAsync(EncuestaRequest r, int actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Titulo)
            || r.Titulo.Length > 150
            || string.IsNullOrWhiteSpace(r.Periodo)
            || r.Periodo.Length > 20)
            throw new BusinessRuleException("Indica título y periodo (por ejemplo 2026-S2).");

        if (r.FechaFin <= r.FechaInicio)
            throw new BusinessRuleException("La fecha de cierre debe ser posterior a la de inicio.");

        var preguntas = r.Preguntas is { Count: > 0 } p ? p : PreguntasBase.ToList();

        if (preguntas.Count > 30 || preguntas.Select(x => x.Id).Distinct().Count() != preguntas.Count
            || preguntas.Any(x => string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 10
                || string.IsNullOrWhiteSpace(x.Texto) || x.Texto.Length > 300 || !Dimensiones.ContainsKey(x.Dimension)))
            throw new BusinessRuleException("Revisa las preguntas: hasta 30, con identificador único, texto y una dimensión válida.");

        var e = new EncuestaCultura {
            Titulo = r.Titulo.Trim(),
            Periodo = r.Periodo.Trim(),
            FechaInicio = r.FechaInicio.ToUniversalTime(),
            FechaFin = r.FechaFin.ToUniversalTime(),
            Preguntas = JsonSerializer.Serialize(preguntas),
            CreadoPor = actor
        };

        db.Set<EncuestaCultura>().Add(e);
        await db.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(
            "CrearEncuestaCultura",
            true,
            actor,
            entidad: "EncuestaCultura",
            idEntidadAfectada: e.Id.ToString(),
            detalles: new { e.Titulo, e.Periodo });

        return e.Id;
    }

    public async Task CambiarEstadoAsync(int id, string estado, int actor, CancellationToken ct)
    {
        // candado global: evita dos encuestas abiertas a la vez
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.BloquearAsync(Bloqueos.Encuestas, 0, ct);

        var e = await db.Set<EncuestaCultura>().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new BusinessRuleException("La encuesta no existe.", BusinessError.NotFound);
        var valido = (e.Estado, estado) is ("borrador", "abierta") or ("abierta", "cerrada");

        if (!valido)
            throw new BusinessRuleException("Solo se puede abrir un borrador o cerrar una encuesta abierta.", BusinessError.Conflict);
        if (estado == "abierta" && await db.Set<EncuestaCultura>().AnyAsync(x => x.Estado == "abierta", ct))
            throw new BusinessRuleException("Ya hay una encuesta abierta. Ciérrala antes de abrir otra.", BusinessError.Conflict);

        e.Estado = estado;

        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync(
            estado == "abierta" ? "AbrirEncuestaCultura" : "CerrarEncuestaCultura",
            true,
            actor,
            entidad: "EncuestaCultura",
            idEntidadAfectada: id.ToString());

        await tx.CommitAsync(ct);
    }

    private static List<PreguntaEncuesta> Preguntas(EncuestaCultura e) =>
        JsonSerializer.Deserialize<List<PreguntaEncuesta>>(e.Preguntas) ?? [];

    public async Task<object?> PendienteAsync(int usuario, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var e = await db.Set<EncuestaCultura>().AsNoTracking().FirstOrDefaultAsync(x => x.Estado == "abierta"
            && x.FechaInicio <= now
            && x.FechaFin > now
            && !db.Set<ParticipacionEncuesta>().Any(p => p.IdEncuesta == x.Id && p.IdUsuario == usuario), ct);

        return e is null ? null : new { e.Id, e.Titulo, e.FechaFin, preguntas = Preguntas(e).Select(p => new { p.Id, p.Texto }) };
    }

    public async Task ResponderAsync(int usuario, int id, Dictionary<string, int> respuestas, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var e = await db.Set<EncuestaCultura>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ??
            throw new BusinessRuleException("La encuesta no existe.", BusinessError.NotFound);

        if (e.Estado != "abierta" || e.FechaInicio > now || e.FechaFin <= now)
            throw new BusinessRuleException("La encuesta no está disponible.", BusinessError.Conflict);

        var preguntas = Preguntas(e);

        if (respuestas is null
            || respuestas.Count != preguntas.Count
            || preguntas.Any(p => !respuestas.TryGetValue(p.Id, out var v) || v is < 1 or > 5))
            throw new BusinessRuleException("Responde todas las preguntas con un valor de 1 a 5.");

        var persona = await db.Users.AsNoTracking()
            .Where(u => u.Id == usuario)
            .Select(u => new { u.Id_Sucursal, u.Id_Area })
            .SingleAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Set<ParticipacionEncuesta>().Add(new ParticipacionEncuesta { IdEncuesta = id, IdUsuario = usuario });
        db.Set<RespuestaEncuesta>().Add(new RespuestaEncuesta {
            IdEncuesta = id,
            IdSucursal = persona.Id_Sucursal,
            IdArea = persona.Id_Area,
            Respuestas = JsonSerializer.Serialize(
                preguntas.ToDictionary(p => p.Id, p => respuestas[p.Id])),
            Fecha = DateOnly.FromDateTime(now.AddHours(-4))
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error)
        when (error.EsDuplicado())
        {
            throw new BusinessRuleException("Ya respondiste esta encuesta. Gracias.", BusinessError.Conflict);
        }

        await tx.CommitAsync(ct);
    }

    private sealed record Fila(int? IdSucursal, int? IdArea, Dictionary<string, int> Valores);

    private static object Medir(IReadOnlyCollection<Fila> filas, List<PreguntaEncuesta> preguntas) => new {
        respuestas = filas.Count,

        porDimension = Dimensiones.Where(d => preguntas.Any(p => p.Dimension == d.Key)).Select(d =>
        {
            var valores = filas
                .SelectMany(f => preguntas
                    .Where(p => p.Dimension == d.Key && f.Valores.ContainsKey(p.Id))
                    .Select(p => f.Valores[p.Id]))
                .ToList();

            return new {
                dimension = d.Key,
                nombre = d.Value,
                promedio = valores.Count > 0 ? Math.Round(valores.Average(), 2) : (double?)null,
                favorable = valores.Count > 0
                    ? Math.Round(100.0 * valores.Count(v => v >= 4) / valores.Count, 1)
                    : (double?)null
            };
        }).ToList(),
    };

    public async Task<object> ResultadosAsync(int id, CancellationToken ct)
    {
        var e = await db.Set<EncuestaCultura>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ??
            throw new BusinessRuleException("La encuesta no existe.", BusinessError.NotFound);

        var umbral = (await gobernanza.PoliticaAsync(ct)).UmbralAgregacion;

        var preguntas = Preguntas(e);

        var filas = (await db.Set<RespuestaEncuesta>().AsNoTracking().Where(r => r.IdEncuesta == id).Select(r => new {
            r.IdSucursal,
            r.IdArea,
            r.Respuestas
        }).ToListAsync(ct))
            .Select(r => new Fila(
                r.IdSucursal,
                r.IdArea,
                JsonSerializer.Deserialize<Dictionary<string, int>>(r.Respuestas) ?? [])).ToList();

        var now = DateTime.UtcNow;

        var poblacion = await db.Users.CountAsync(u => u.Activo && db.RolUsuario.Any(r => r.IdUsuario == u.Id
            && r.Activo
            && r.Rol.Nombre == RolesSistemas.RolColaborador
            && r.FechaInicio <= now
            && (r.FechaFin == null || r.FechaFin > now)), ct);

        var suficientes = filas.Count >= umbral;
        // supresion complementaria: un grupo pequeno no puede deducirse por diferencia
        var (sucursalesVisibles, areasVisibles) = Estadistica.GruposVisiblesCruzados(
            filas.Select(f => (f.IdSucursal, f.IdArea)).ToList(),
            umbral);

        var sucursales = await db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.Id_Sucursal, s => s.Nombre, ct);
        var areas = await db.AreasOperativas.AsNoTracking().ToDictionaryAsync(a => a.Id_Area, a => a.Nombre, ct);

        // comparacion con la encuesta cerrada anterior (mismas dimensiones)
        var anterior = await db.Set<EncuestaCultura>().AsNoTracking()
            .Where(x => x.Id != id && x.Estado == "cerrada" && x.FechaFin <= e.FechaInicio)
            .OrderByDescending(x => x.FechaFin)
            .FirstOrDefaultAsync(ct);
        object? comparacion = null;

        if (anterior is not null && suficientes)
        {
            var filasAnt = (await db.Set<RespuestaEncuesta>()
                .AsNoTracking()
                .Where(r => r.IdEncuesta == anterior.Id)
                .Select(r => r.Respuestas).ToListAsync(ct))
                .Select(r => new Fila(null, null, JsonSerializer.Deserialize<Dictionary<string, int>>(r) ?? []))
                .ToList();

            if (filasAnt.Count >= umbral)
            {
                var pAnt = Preguntas(anterior);
                double? Prom(List<Fila> fs, List<PreguntaEncuesta> ps, string dim)
                {
                    var v = fs
                        .SelectMany(f => ps
                            .Where(p => p.Dimension == dim && f.Valores.ContainsKey(p.Id))
                            .Select(p => f.Valores[p.Id]))
                        .ToList();

                    return v.Count > 0 ? v.Average() : null;
                }

                comparacion = new {
                    anterior = new {
                        anterior.Id,
                        anterior.Titulo,
                        anterior.Periodo,
                        respuestas = filasAnt.Count
                    },
                    diferencias = Dimensiones.Select(d => new {
                        dimension = d.Key,
                        nombre = d.Value,
                        diferencia = Prom(filas, preguntas, d.Key) is { } a && Prom(filasAnt, pAnt, d.Key) is { } b
                            ? Math.Round(a - b, 2)
                            : (double?)null
                    }).ToList()
                };
            }
        }

        return new {
            e.Id,
            e.Titulo,
            e.Periodo,
            e.Estado,
            e.FechaInicio,
            e.FechaFin,
            umbral,
            poblacion,

            participacion = poblacion > 0 ? Math.Round(100.0 * filas.Count / poblacion, 1) : (double?)null,

            general = suficientes ? Medir(filas, preguntas) : new { respuestas = filas.Count, oculto = true },

            porPregunta = suficientes ? preguntas.Select(p =>
            {
                var v = filas.Where(f => f.Valores.ContainsKey(p.Id)).Select(f => f.Valores[p.Id]).ToList();

                return new {
                    p.Id,
                    p.Texto,
                    p.Dimension,
                    promedio = v.Count > 0 ? Math.Round(v.Average(), 2) : (double?)null,
                    distribucion = Enumerable.Range(1, 5).Select(n => v.Count(x => x == n)).ToArray()
                };
            }).ToList() : null,

            porSucursal = filas
                .GroupBy(f => f.IdSucursal)
                .Where(g => sucursalesVisibles.Contains(g.Key))
                .Select(g => new {
                    sucursal = g.Key is { } s ? sucursales.GetValueOrDefault(s) : "Sin sucursal",
                    medicion = Medir(g.ToList(), preguntas)
                }).ToList(),

            porArea = filas
                .GroupBy(f => f.IdArea)
                .Where(g => areasVisibles.Contains(g.Key))
                .Select(g => new {
                    area = g.Key is { } a ? areas.GetValueOrDefault(a) : "Sin área",
                    medicion = Medir(g.ToList(), preguntas)
                }).ToList(),

            gruposOcultos = filas.GroupBy(f => f.IdSucursal).Count(g => !sucursalesVisibles.Contains(g.Key))
                + filas.GroupBy(f => f.IdArea).Count(g => !areasVisibles.Contains(g.Key)),
            comparacion,

            nota = $"Respuestas anónimas: la participación se registra por separado y sin vínculo con la respuesta. Solo se muestran grupos de {umbral} o más respuestas que no permitan deducir a grupos más pequeños.",
        };
    }
}
