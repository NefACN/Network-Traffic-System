using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed class CyberScoreColaboradoresService(IApplicationDbContext db, CyberScoreService cyberScore, GobernanzaService gobernanza)
{
    private IQueryable<ApplicationUser> Colaboradores(DateTime now) => db.Users.AsNoTracking().Where(u => u.Activo
        && !db.RolUsuario.Any(r => r.IdUsuario == u.Id && r.Activo && r.Rol.Activo && r.FechaInicio <= now
            && (r.FechaFin == null || r.FechaFin > now) && (r.Rol.Nombre == RolesSistemas.RolAdmin || r.Rol.Nombre == RolesSistemas.RolGerente)));

    private static object Distribucion(IEnumerable<CyberScoreService.Resultado> resultados)
    {
        var lista = resultados.ToList();
        return new {
            total = lista.Count,
            niveles = CyberScoreService.Niveles.Select(n => new {
                nivel = n,
                cantidad = lista.Count(r => r.Nivel == n)
            }
            )
                .Append(new {
                    nivel = CyberScoreService.SinEvaluacion,
                    cantidad = lista.Count(r => !r.Evaluable)
                }).ToList(),

            promedio = lista.Where(r => r.Evaluable).Select(r => r.CyberScore!.Value).DefaultIfEmpty().Average() is var a && lista.Any(r => r.Evaluable) ? Math.Round(a, 1) : (decimal?)null,
        };
    }

    // lista paginada para admin
    public async Task<object> ConsultarAsync(int actor, bool admin, int? idSucursal, int? idArea, string? nivel, string? busqueda, CancellationToken ct, int? pagina = null, int? tamanio = null)
    {
        var now = DateTime.UtcNow;
        var q = Colaboradores(now);
        if (!admin)
        {
            // gerente: alcance fijo a su sucursal y sin datos individuales
            idSucursal = await db.Users.Where(u => u.Id == actor).Select(u => u.Id_Sucursal).SingleAsync(ct)
                ?? throw new BusinessRuleException("Tu cuenta no tiene sucursal asignada.", BusinessError.Forbidden);
        }

        if (idSucursal.HasValue)
            q = q.Where(u => u.Id_Sucursal == idSucursal);

        if (idArea.HasValue)
            q = q.Where(u => u.Id_Area == idArea);

        var personas = await q.OrderBy(u => u.Nombre).ThenBy(u => u.Id).Select(u => new {
            u.Id,
            u.Nombre,
            u.Email,
            sucursal = db.Sucursales
                .Where(s => s.Id_Sucursal == u.Id_Sucursal)
                .Select(s => s.Nombre)
                .FirstOrDefault(),
            area = u.AreaOperativa != null ? u.AreaOperativa.Nombre : null
        }).ToListAsync(ct);

        var resultados = (await cyberScore.ResultadosAsync(personas.Select(p => p.Id).ToList(), ct)).ToDictionary(r => r.IdUsuario);

        var politica = await cyberScore.PoliticaAsync(ct);

        var umbrales = new { bajo = politica.UmbralBajo, medio = politica.UmbralMedio, alto = politica.UmbralAlto };

        if (!admin)
        {
            var minimo = (await gobernanza.PoliticaAsync(ct)).UmbralAgregacion;
            var areas = await PorAreaAsync(idSucursal, minimo, ct);

            var oculto = resultados.Count < minimo || (idArea.HasValue && !areas.Any(a => a.IdArea == idArea && !a.Oculto));

            return new {
                individual = false,
                umbrales,
                umbralAgregacion = minimo,
                oculto,
                areas,
                distribucion = oculto ? null : Distribucion(resultados.Values),
                nota = oculto ? $"Esta selección podría identificar a grupos de menos de {minimo} colaboradores; la distribución se oculta para proteger su identidad." : "Distribución agregada de tu sucursal. No incluye datos individuales."
            };
        }

        var planes = await db.Set<AsignacionMicroleccion>().AsNoTracking()
            .Where(a => a.Motivo == PlanRefuerzoService.Motivo && a.FechaCompletado == null)
            .GroupBy(a => a.IdUsuario)
            .Select(g => new { g.Key, cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.cantidad, ct);

        var items = personas.Select(p =>
        {
            var r = resultados[p.Id];

            return new {
                id = p.Id,
                nombre = p.Nombre,
                correo = p.Email,
                p.sucursal,
                p.area,
                cyberScore = r.CyberScore,
                r.Nivel,
                r.Riesgo,
                r.Evaluable,
                r.Simulaciones,
                r.Clics,
                r.Credenciales,
                r.Reportes,
                planPendiente = planes.GetValueOrDefault(p.Id)
            };
        }).ToList();

        var texto = busqueda?.Trim().ToLowerInvariant();

        var filtrados = items.Where(i => (string.IsNullOrEmpty(nivel) || i.Nivel == nivel)
            && (string.IsNullOrEmpty(texto) || i.nombre.ToLowerInvariant().Contains(texto) || (i.correo ?? "").ToLowerInvariant().Contains(texto)))
            // primero quienes necesitan refuerzo
            .OrderBy(i => i.Evaluable ? 0 : 1)
            .ThenBy(i => i.cyberScore ?? 101)
            .ThenBy(i => i.nombre)
            .ThenBy(i => i.id)
            .Paginar(pagina, tamanio);

        return new {
            individual = true,
            umbrales,
            distribucion = Distribucion(resultados.Values),
            areas = await PorAreaAsync(idSucursal, 0, ct),
            items = filtrados.Items,
            total = filtrados.Total,
            pagina = filtrados.Pagina,
            tamanio = filtrados.Tamanio
        };
    }


    public sealed record ResumenArea(int IdArea, string Area, string? Criticidad, int Colaboradores, int Evaluados, decimal? Promedio, string Nivel,
        int Bajo, int Medio, int Alto, int Critico, int SinEvaluacion, decimal? PorcentajeAltoCritico, int PlanesPendientes, bool Oculto);


    public async Task<List<ResumenArea>> PorAreaAsync(int? idSucursal, int umbralAgregacion, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var q = Colaboradores(now).Where(u => u.Id_Area != null);

        if (idSucursal.HasValue)
            q = q.Where(u => u.Id_Sucursal == idSucursal);

        var personas = await q.Select(u => new { u.Id, idArea = u.Id_Area!.Value }).ToListAsync(ct);

        var areas = await db.AreasOperativas.AsNoTracking().Where(a => a.Activo || personas.Select(p => p.idArea).Contains(a.Id_Area))
            .Select(a => new { a.Id_Area, a.Nombre, criticidad = a.Criticidad.Nivel }).ToListAsync(ct);

        var resultados = (await cyberScore.ResultadosAsync(personas.Select(p => p.Id).ToList(), ct)).ToDictionary(r => r.IdUsuario);

        var politica = await cyberScore.PoliticaAsync(ct);

        var ids = personas.Select(p => p.Id).ToList();

        var planes = await db.Set<AsignacionMicroleccion>().AsNoTracking()
            .Where(a => a.Motivo == PlanRefuerzoService.Motivo && a.FechaCompletado == null && ids.Contains(a.IdUsuario))
            .Select(a => a.IdUsuario).ToListAsync(ct);

        var visibles = umbralAgregacion > 0
            ? Estadistica.GruposVisibles(personas.GroupBy(p => p.idArea).Select(g => (g.Key, g.Count())),
                await SinAreaAsync(idSucursal, now, ct), umbralAgregacion)
            : null;

        return areas.Select(a =>
        {
            var miembros = personas.Where(p => p.idArea == a.Id_Area).Select(p => resultados[p.Id]).ToList();
            var evaluados = miembros.Where(r => r.Evaluable).ToList();
            var promedio = evaluados.Count > 0 ? Math.Round(evaluados.Average(r => r.CyberScore!.Value), 1) : (decimal?)null;
            int Cuenta(string nivel) => evaluados.Count(r => r.Nivel == nivel);
            var oculto = visibles is not null && !visibles.Contains(a.Id_Area);
            var altoCritico = Cuenta("Alto") + Cuenta("Crítico");

            return oculto
                ? new ResumenArea(a.Id_Area, a.Nombre, a.criticidad, miembros.Count, 0, null, "Oculto", 0, 0, 0, 0, 0, null, 0, true)
                : new ResumenArea(a.Id_Area, a.Nombre, a.criticidad, miembros.Count, evaluados.Count, promedio,
                    promedio is { } valor ? CyberScoreService.Nivel(politica, valor) : CyberScoreService.SinEvaluacion,
                    Cuenta("Bajo"), Cuenta("Medio"), Cuenta("Alto"), Cuenta("Crítico"), miembros.Count - evaluados.Count,
                    evaluados.Count > 0 ? Math.Round(100m * altoCritico / evaluados.Count, 1) : null,
                    planes.Count(id => miembros.Any(m => m.IdUsuario == id)), false);

        }).Where(r => r.Colaboradores > 0 || umbralAgregacion == 0)
            // primero las areas que requieren refuerzo
            .OrderBy(r => r.Promedio is null ? 1 : 0).ThenBy(r => r.Promedio).ThenBy(r => r.Area).ToList();
    }

    private Task<int> SinAreaAsync(int? idSucursal, DateTime now, CancellationToken ct)
    {
        var q = Colaboradores(now).Where(u => u.Id_Area == null);

        if (idSucursal.HasValue)
            q = q.Where(u => u.Id_Sucursal == idSucursal);

        return q.CountAsync(ct);
    }

    public async Task<object> DetalleAsync(int id, CancellationToken ct)
    {
        var persona = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new {
                u.Id,
                u.Nombre,
                u.Email,
                sucursal = db.Sucursales.Where(s => s.Id_Sucursal == u.Id_Sucursal).Select(s => s.Nombre).FirstOrDefault(),
                area = u.AreaOperativa != null ? u.AreaOperativa.Nombre : null
            }).SingleOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("Colaborador no encontrado.", BusinessError.NotFound);

        var plan = await db.Set<AsignacionMicroleccion>().AsNoTracking().Where(a => a.IdUsuario == id && a.Motivo == PlanRefuerzoService.Motivo)
            .OrderByDescending(a => a.FechaAsignacion).Take(20)
            .Select(a => new { a.Actividad.Titulo, a.FechaAsignacion, a.FechaCompletado }).ToListAsync(ct);

        return new { persona, cyberScore = await cyberScore.CalcularAsync(id, ct), planRefuerzo = plan };
    }
}
