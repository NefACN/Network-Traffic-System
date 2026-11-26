using System.Net;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed record RequisitoRequest(int IdActividad, bool EsInduccion, int PlazoDias, int FrecuenciaDias);
public sealed record RutaRequest(string Nombre, string? Descripcion, int? IdArea, bool Activa, List<RequisitoRequest> Requisitos);

// rutas de aprendizaje por area: induccion, refrescos y vencimientos (ISO/IEC 27001 7.2-7.3)
public sealed class RutasAprendizajeService(IApplicationDbContext db, IAuditoriaService auditoria, IEmailSender correo)
{
    public const int DiasAviso = 15, DiasEntreRecordatorios = 7;

    // estado de un requisito para una persona, funcion pura para poder verificarla
    public static (string Estado, DateTime? Vence) Evaluar(
        RutaRequisito r,
        DateTime altaPersona,
        DateTime? ultimaAprobacion,
        DateTime ahora)
    {
        if (ultimaAprobacion is { } ultima)
        {
            if (r.FrecuenciaDias == 0)
                return ("al_dia", null);

            var refresco = ultima.AddDays(r.FrecuenciaDias);
            return (ahora > refresco
                ? "vencido"
                : refresco - ahora <= TimeSpan.FromDays(DiasAviso) ? "por_vencer" : "al_dia", refresco);
        }

        // la induccion corre desde el alta, el resto desde que el requisito esta vigente
        var inicio = r.EsInduccion ? (altaPersona > r.VigenteDesde ? altaPersona : r.VigenteDesde) : r.VigenteDesde;
        var vence = inicio.AddDays(r.PlazoDias);
        return (ahora > vence ? "vencido" : vence - ahora <= TimeSpan.FromDays(DiasAviso) ? "por_vencer" : "pendiente", vence);
    }

    // rutas paginadas por nombre
    public async Task<object> ListarAsync(CancellationToken ct, int? pagina = null, int? tamanio = null) =>
        await db.Set<RutaAprendizaje>().AsNoTracking().OrderBy(r => r.Nombre).ThenBy(r => r.Id).Select(r => new {
            r.Id,
            r.Nombre,
            r.Descripcion,
            r.IdArea,
            area = db.AreasOperativas.Where(a => a.Id_Area == r.IdArea).Select(a => a.Nombre).FirstOrDefault(),
            r.Activa,
            requisitos = r.Requisitos.OrderByDescending(q => q.EsInduccion).ThenBy(q => q.Id).Select(q => new {
                q.Id,
                q.IdActividad,
                actividad = q.Actividad.Titulo,
                tema = q.Actividad.Categoria.Nombre,
                habilitada = q.Actividad.Habilitada,
                q.EsInduccion,
                q.PlazoDias,
                q.FrecuenciaDias,
                q.VigenteDesde,
            }).ToList(),
        }).PaginarAsync(pagina, tamanio, ct);

    public async Task<int> GuardarAsync(int? id, RutaRequest r, int actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length > 120)
            throw new BusinessRuleException("Indica el nombre de la ruta (máximo 120 caracteres).");

        var requisitos = r.Requisitos ?? [];
        if (requisitos.Count == 0 || requisitos.Count > 30)
            throw new BusinessRuleException("Incluye entre 1 y 30 actividades en la ruta.");
        if (requisitos.Select(x => x.IdActividad).Distinct().Count() != requisitos.Count)
            throw new BusinessRuleException("Cada actividad puede figurar una sola vez en la ruta.");
        if (requisitos.Any(x => x.PlazoDias is < 1 or > 365
            || x.FrecuenciaDias is < 0 or > 730
            || (x.FrecuenciaDias is > 0 and < 30)))
            throw new BusinessRuleException("El plazo debe estar entre 1 y 365 días y la frecuencia ser 0 (una sola vez) o entre 30 y 730 días.");

        var ids = requisitos.Select(x => x.IdActividad).ToList();
        if (await db.Set<ActividadEducativa>().CountAsync(a => ids.Contains(a.Id) && a.Habilitada, ct) != ids.Count)
            throw new BusinessRuleException("Todas las actividades de la ruta deben existir y estar habilitadas.");
        if (r.IdArea.HasValue && !await db.AreasOperativas.AnyAsync(a => a.Id_Area == r.IdArea, ct))
            throw new BusinessRuleException("El área no existe.");

        RutaAprendizaje ruta;
        if (id.HasValue)
            ruta = await db.Set<RutaAprendizaje>().Include(x => x.Requisitos).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new BusinessRuleException("La ruta no existe.", BusinessError.NotFound);
        else
        {
            ruta = new RutaAprendizaje();
            db.Set<RutaAprendizaje>().Add(ruta);
        }

        ruta.Nombre = r.Nombre.Trim();
        ruta.Descripcion = string.IsNullOrWhiteSpace(r.Descripcion)
            ? null
            : r.Descripcion.Trim()[..Math.Min(500, r.Descripcion.Trim().Length)];
        ruta.IdArea = r.IdArea;
        ruta.Activa = r.Activa;

        var now = DateTime.UtcNow;
        db.Set<RutaRequisito>().RemoveRange(ruta.Requisitos.Where(q => !ids.Contains(q.IdActividad)));
        foreach (var q in requisitos)
        {
            var existente = ruta.Requisitos.FirstOrDefault(x => x.IdActividad == q.IdActividad);
            // un requisito nuevo empieza a contar hoy, no vence retroactivamente
            if (existente is null)
                ruta.Requisitos.Add(new RutaRequisito {
                    IdActividad = q.IdActividad,
                    EsInduccion = q.EsInduccion,
                    PlazoDias = q.PlazoDias,
                    FrecuenciaDias = q.FrecuenciaDias,
                    VigenteDesde = now
                });
            else
            {
                existente.EsInduccion = q.EsInduccion;
                existente.PlazoDias = q.PlazoDias;
                existente.FrecuenciaDias = q.FrecuenciaDias;
            }
        }

        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync(id.HasValue ? "EditarRutaAprendizaje" : "CrearRutaAprendizaje", true, actor, entidad: "RutaAprendizaje",
            idEntidadAfectada: ruta.Id.ToString(), detalles: new { ruta.Nombre, ruta.IdArea, ruta.Activa, requisitos = requisitos.Count });

        return ruta.Id;
    }

    public sealed record ItemRuta(int IdUsuario, int IdRuta, string Ruta, int IdActividad, string Actividad, string Tema, bool EsInduccion,
        int FrecuenciaDias, string Estado, DateTime? Vence, DateTime? UltimaAprobacion);

    // estado de los requisitos de las personas indicadas o de todo el personal activo
    public async Task<List<ItemRuta>> EstadosAsync(int? usuario, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var personas = await db.Users.AsNoTracking()
            .Where(u => u.Activo && (usuario == null || u.Id == usuario)
                && db.RolUsuario.Any(r => r.IdUsuario == u.Id
                    && r.Activo
                    && r.Rol.Nombre == RolesSistemas.RolColaborador
                    && r.FechaInicio <= ahora
                    && (r.FechaFin == null || r.FechaFin > ahora)))
            .Select(u => new { u.Id, u.Id_Area, u.FechaCreacion }).ToListAsync(ct);
        var rutas = await db.Set<RutaAprendizaje>().AsNoTracking().Where(r => r.Activa)
            .Select(r => new {
                r.Id,
                r.Nombre,
                r.IdArea,
                requisitos = r.Requisitos.Where(q => q.Actividad.Habilitada)
                    .Select(q => new {
                        q.Id,
                        q.IdActividad,
                        q.EsInduccion,
                        q.PlazoDias,
                        q.FrecuenciaDias,
                        q.VigenteDesde,
                        actividad = q.Actividad.Titulo,
                        tema = q.Actividad.Categoria.Nombre
                    }).ToList()
            })
            .ToListAsync(ct);

        var ids = personas.Select(p => p.Id).ToList();
        var actividades = rutas.SelectMany(r => r.requisitos.Select(q => q.IdActividad)).Distinct().ToList();
        var aprobaciones = (await db.IntentoCursoVoluntario.AsNoTracking()
                .Where(i => ids.Contains(i.Id_Usuario)
                    && i.IdActividad != null
                    && actividades.Contains(i.IdActividad.Value)
                    && i.Exitoso == true
                    && i.FechaCompletado != null)
                .GroupBy(i => new { i.Id_Usuario, i.IdActividad })
                .Select(g => new { g.Key.Id_Usuario, g.Key.IdActividad, ultima = g.Max(i => i.FechaCompletado) })
                .ToListAsync(ct))
            .ToDictionary(x => (x.Id_Usuario, x.IdActividad!.Value), x => x.ultima);

        var items = new List<ItemRuta>();
        foreach (var p in personas)
            foreach (var r in rutas.Where(r => r.IdArea == null || r.IdArea == p.Id_Area))
                foreach (var q in r.requisitos)
                {
                    var ultima = aprobaciones.GetValueOrDefault((p.Id, q.IdActividad));
                    var (estado, vence) = Evaluar(
                        new RutaRequisito {
                            EsInduccion = q.EsInduccion,
                            PlazoDias = q.PlazoDias,
                            FrecuenciaDias = q.FrecuenciaDias,
                            VigenteDesde = q.VigenteDesde
                        },
                        p.FechaCreacion, ultima, ahora);
                    items.Add(new ItemRuta(
                        p.Id,
                        r.Id,
                        r.Nombre,
                        q.IdActividad,
                        q.actividad,
                        q.tema,
                        q.EsInduccion,
                        q.FrecuenciaDias,
                        estado,
                        vence,
                        ultima));
                }

        return items;
    }

    public async Task<object> MiRutaAsync(int usuario, CancellationToken ct)
    {
        var items = await EstadosAsync(usuario, ct);
        var orden = new Dictionary<string, int> { ["vencido"] = 0, ["por_vencer"] = 1, ["pendiente"] = 2, ["al_dia"] = 3 };

        return new {
            total = items.Count,
            alDia = items.Count(i => i.Estado == "al_dia"),
            items = items
                .OrderBy(i => orden[i.Estado])
                .ThenBy(i => i.Vence)
                .Select(i => new {
                    i.Ruta,
                    i.IdActividad,
                    i.Actividad,
                    i.Tema,
                    i.EsInduccion,
                    i.FrecuenciaDias,
                    i.Estado,
                    i.Vence,
                    i.UltimaAprobacion
                }),
        };
    }

    // indicadores de cumplimiento con lista paginada de atrasados
    public async Task<object> CumplimientoAsync(CancellationToken ct, int? pagina = null, int? tamanio = null)
    {
        var items = await EstadosAsync(null, ct);
        var personas = await db.Users.AsNoTracking().Select(u => new {
            u.Id,
            nombre = u.Nombre + " " + u.Apellido,
            u.Email,
            sucursal = u.Sucursal != null ? u.Sucursal.Nombre : null,
            area = u.AreaOperativa != null ? u.AreaOperativa.Nombre : null
        }).ToDictionaryAsync(u => u.Id, ct);
        static decimal? Tasa(int a, int b) => b > 0 ? Math.Round(100m * a / b, 1) : null;

        return new {
            total = items.Count,
            alDia = items.Count(i => i.Estado == "al_dia"),
            pendientes = items.Count(i => i.Estado == "pendiente"),
            porVencer = items.Count(i => i.Estado == "por_vencer"),
            vencidos = items.Count(i => i.Estado == "vencido"),
            cumplimiento = Tasa(items.Count(i => i.Estado == "al_dia"), items.Count),
            personas = items.Select(i => i.IdUsuario).Distinct().Count(),
            porRuta = items.GroupBy(i => new { i.IdRuta, i.Ruta }).Select(g => new {
                g.Key.IdRuta,
                g.Key.Ruta,
                total = g.Count(),
                alDia = g.Count(i => i.Estado == "al_dia"),
                vencidos = g.Count(i => i.Estado == "vencido"),
                cumplimiento = Tasa(g.Count(i => i.Estado == "al_dia"), g.Count())
            }).OrderBy(r => r.Ruta).ToList(),
            porArea = items.GroupBy(i => personas.GetValueOrDefault(i.IdUsuario)?.area ?? "Sin área").Select(g => new {
                area = g.Key,
                total = g.Count(),
                cumplimiento = Tasa(g.Count(i => i.Estado == "al_dia"), g.Count()),
                vencidos = g.Count(i => i.Estado == "vencido")
            }).OrderBy(a => a.area).ToList(),
            atrasados = items
                .Where(i => i.Estado is "vencido" or "por_vencer")
                .OrderBy(i => i.Vence)
                .ThenBy(i => i.IdUsuario)
                .ThenBy(i => i.IdActividad)
                .ThenBy(i => i.Ruta)
                .Select(i => new {
                    i.IdUsuario,
                    nombre = personas.GetValueOrDefault(i.IdUsuario)?.nombre,
                    sucursal = personas.GetValueOrDefault(i.IdUsuario)?.sucursal,
                    area = personas.GetValueOrDefault(i.IdUsuario)?.area,
                    i.Ruta,
                    i.Actividad,
                    i.Estado,
                    i.Vence,
                }).Paginar(pagina, tamanio),
        };
    }

    // un correo por persona con vencidos o proximos, maximo uno cada 7 dias
    public async Task<object> EnviarRecordatoriosAsync(int? actor, CancellationToken ct)
    {
        var items = (await EstadosAsync(null, ct))
            .Where(i => i.Estado is "vencido" or "por_vencer")
            .GroupBy(i => i.IdUsuario)
            .ToList();
        var limite = DateTime.UtcNow.AddDays(-DiasEntreRecordatorios);
        var recientes = (await db.Set<RecordatorioAprendizaje>()
                .Where(r => r.Fecha >= limite)
                .Select(r => r.IdUsuario)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        var ids = items.Select(g => g.Key).Where(id => !recientes.Contains(id)).ToList();
        var personas = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Id, u.Nombre, u.Apellido, u.Email })
            .ToListAsync(ct);

        int enviados = 0, fallidos = 0;
        foreach (var p in personas)
        {
            // si el envio se interrumpe, lo ya enviado queda registrado y no se repite
            ct.ThrowIfCancellationRequested();
            var lista = items.Single(g => g.Key == p.Id).OrderBy(i => i.Vence).ToList();
            var filas = string.Join("", lista.Select(i =>
                $"<li>{WebUtility.HtmlEncode(i.Actividad)} ({WebUtility.HtmlEncode(i.Ruta)}): {(i.Estado == "vencido" ? "vencida" : "vence")} el {i.Vence:dd/MM/yyyy}</li>"));
            try
            {
                await correo.EnviarAsync(p.Email!, $"{p.Nombre} {p.Apellido}", "Tienes actividades de seguridad pendientes",
                    $"<p>Hola {WebUtility.HtmlEncode(p.Nombre)},</p><p>Estas actividades de tu ruta de aprendizaje requieren atención:</p><ul>{filas}</ul><p>Encuéntralas en <em>Mi aprendizaje</em>. Son breves y te ayudan a protegerte.</p>");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fallidos++;
                continue;
            }

            // el correo ya salio: se registra sin cancelacion para respetar el limite de 7 dias
            db.Set<RecordatorioAprendizaje>().Add(new RecordatorioAprendizaje { IdUsuario = p.Id, Pendientes = lista.Count });
            await db.SaveChangesAsync(CancellationToken.None);
            enviados++;
        }

        if (actor.HasValue)
            await auditoria.RegistrarAsync("RecordatoriosAprendizaje", true, actor, entidad: "RutaAprendizaje",
                detalles: new { enviados, fallidos, omitidosPorRecientes = items.Count - ids.Count });

        return new { enviados, fallidos, omitidosPorRecientes = items.Count - ids.Count, personasConPendientes = items.Count };
    }
}
