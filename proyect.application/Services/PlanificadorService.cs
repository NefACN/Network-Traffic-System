using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Entidades;

namespace proyect.application.Services;

public sealed record EventoPlanRequest(
    int Anio,
    int Mes,
    string Titulo,
    string Tipo,
    int? IdCategoria,
    int? IdSucursal,
    string? Descripcion,
    string Estado = "planificado",
    int? IdCampania = null);

// planificador anual del programa
public sealed class PlanificadorService(IApplicationDbContext db, IAuditoriaService auditoria)
{
    public static readonly IReadOnlyList<string> Tipos = ["campania", "capacitacion", "encuesta", "comunicacion", "temporada"];
    public static readonly IReadOnlyList<string> Estados = ["planificado", "realizado", "cancelado"];

    public sealed record Temporada(string Clave, int Mes, string Titulo, string Recomendacion);

    // temporadas comerciales y fiscales con mas intentos de fraude, editables
    public static readonly IReadOnlyList<Temporada> TemporadasBolivia =
    [
        new(
            "rebajas-inicio-anio",
            1,
            "Rebajas de inicio de año",
            "Ofertas y cupones falsos por correo y redes. Refuerza la verificación de remitentes antes de la temporada."),
        new(
            "inicio-clases",
            2,
            "Inicio de clases",
            "Alta demanda de útiles y tecnología: tiendas y promociones falsas. Buen momento para una campaña sobre enlaces acortados."),
        new("carnaval", 2, "Carnaval", "Sorteos y premios falsos en mensajería. Capacitación breve sobre mensajes de WhatsApp y SMS."),
        new(
            "dia-del-padre",
            3,
            "Día del Padre (19 de marzo)",
            "Pico de ventas: suplantación de proveedores y pagos. Refuerza la verificación de cambios de cuenta bancaria."),
        new(
            "declaraciones-impositivas",
            4,
            "Declaraciones impositivas",
            "Suplantación de la administración tributaria y de contadores. Campaña sobre adjuntos y facturas falsas para Finanzas y Contabilidad."),
        new(
            "dia-del-nino",
            4,
            "Día del Niño (12 de abril)",
            "Promociones y concursos falsos. Comunicación preventiva al personal de ventas."),
        new(
            "dia-de-la-madre",
            5,
            "Día de la Madre (27 de mayo)",
            "Una de las temporadas de mayor venta: fraude en pagos QR, comprobantes falsos y suplantación de clientes en caja."),
        new(
            "fiestas-patrias",
            8,
            "Fiestas patrias (6 de agosto)",
            "Promociones y feriados largos: aumentan los intentos fuera de horario. Revisa los procedimientos de reporte."),
        new(
            "black-friday",
            11,
            "Black Friday y Cyber Monday",
            "Mayor volumen de fraude en compras en línea y falsas confirmaciones de pedido. Campaña para E-Commerce y Atención al Cliente."),
        new(
            "navidad-cierre",
            12,
            "Navidad, aguinaldo y cierre de gestión",
            "Fraudes sobre aguinaldo y regalos. Hazlo como capacitación: la política ética prohíbe usar el aguinaldo como pretexto de simulación."),
    ];

    public async Task<object> AnioAsync(int anio, CancellationToken ct)
    {
        if (anio is < 2020 or > 2100)
            throw new BusinessRuleException("Selecciona un año válido.");

        var eventos = await db.Set<EventoPlan>().AsNoTracking()
            .Where(e => e.Anio == anio)
            .OrderBy(e => e.Mes)
            .ThenBy(e => e.Id)
            .Select(e => new {
                e.Id,
                e.Anio,
                e.Mes,
                e.Titulo,
                e.Tipo,
                e.IdCategoria,
                tema = db.CategoriasPregunta.Where(c => c.IdCategoria == e.IdCategoria).Select(c => c.Nombre).FirstOrDefault(),
                e.IdSucursal,
                sucursal = db.Sucursales.Where(s => s.Id_Sucursal == e.IdSucursal).Select(s => s.Nombre).FirstOrDefault(),
                e.Descripcion,
                e.Estado,
                e.Temporada,
                e.IdCampania,
                campania = db.CampaniasSimulacion.Where(c => c.Id_Campania == e.IdCampania).Select(c => c.Titulo).FirstOrDefault(),
            }).ToListAsync(ct);

        var activos = eventos.Where(e => e.Estado != "cancelado" && e.Tipo != "temporada").ToList();

        var temas = await db.CategoriasPregunta.AsNoTracking().Select(c => new { c.IdCategoria, c.Nombre }).ToListAsync(ct);
        var cargadas = eventos.Where(e => e.Temporada != null).Select(e => e.Temporada).ToHashSet();

        return new {
            anio,
            eventos,
            cobertura = new {
                porMes = Enumerable.Range(1, 12)
                    .Select(m => new {
                        mes = m,
                        actividades = activos.Count(e => e.Mes == m),
                        temporadas = eventos.Count(e => e.Mes == m && e.Tipo == "temporada")
                    }).ToList(),

                mesesSinActividad = Enumerable.Range(1, 12)
                    .Where(m => !activos.Any(e => e.Mes == m)).ToList(),

                temasSinCobertura = temas
                    .Where(t => !activos.Any(e => e.IdCategoria == t.IdCategoria))
                    .Select(t => t.Nombre)
                    .ToList(),
                realizados = activos.Count(e => e.Estado == "realizado"),
                planificados = activos.Count(e => e.Estado == "planificado"),
            },
            temporadasDisponibles = TemporadasBolivia
                .Where(t => !cargadas.Contains(t.Clave))
                .Select(t => new { t.Clave, t.Mes, t.Titulo })
                .ToList(),
        };
    }

    private async Task ValidarAsync(EventoPlanRequest r, CancellationToken ct)
    {
        if (r.Anio is < 2020 or > 2100 || r.Mes is < 1 or > 12)
            throw new BusinessRuleException("Indica un mes y año válidos.");

        if (string.IsNullOrWhiteSpace(r.Titulo) || r.Titulo.Trim().Length > 150)
            throw new BusinessRuleException("Indica un título (máximo 150 caracteres).");

        if (!Tipos.Contains(r.Tipo) || !Estados.Contains(r.Estado))
            throw new BusinessRuleException("Selecciona un tipo y un estado válidos.");

        if (r.Descripcion?.Length > 1000)
            throw new BusinessRuleException("La descripción admite hasta 1000 caracteres.");

        if (r.IdCategoria.HasValue && !await db.CategoriasPregunta.AnyAsync(c => c.IdCategoria == r.IdCategoria, ct))
            throw new BusinessRuleException("El tema no existe.");

        if (r.IdSucursal.HasValue && !await db.Sucursales.AnyAsync(s => s.Id_Sucursal == r.IdSucursal, ct))
            throw new BusinessRuleException("La sucursal no existe.");

        if (r.IdCampania.HasValue && !await db.CampaniasSimulacion.AnyAsync(c => c.Id_Campania == r.IdCampania, ct))
            throw new BusinessRuleException("La campaña no existe.");
    }

    public async Task<int> GuardarAsync(int? id, EventoPlanRequest r, int actor, CancellationToken ct)
    {
        await ValidarAsync(r, ct);
        EventoPlan e;

        if (id.HasValue)
            e = await db.Set<EventoPlan>().SingleOrDefaultAsync(x => x.Id == id, ct) ??
                throw new BusinessRuleException("El evento no existe.", BusinessError.NotFound);
        else
        {
            e = new EventoPlan { CreadoPor = actor };
            db.Set<EventoPlan>().Add(e);
        }

        e.Anio = r.Anio;
        e.Mes = r.Mes;
        e.Titulo = r.Titulo.Trim();
        e.Tipo = r.Tipo;
        e.IdCategoria = r.IdCategoria;
        e.IdSucursal = r.IdSucursal;
        e.Descripcion = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim();
        e.Estado = r.Estado;
        e.IdCampania = r.IdCampania;

        await db.SaveChangesAsync(ct);

        await auditoria.RegistrarAsync(
            id.HasValue ? "EditarEventoPlan" : "CrearEventoPlan",
            true,
            actor,
            entidad: "EventoPlan",
            idEntidadAfectada: e.Id.ToString(),
            detalles: new {
                e.Anio,
                e.Mes,
                e.Tipo,
                e.Estado
            });

        return e.Id;
    }

    public async Task EliminarAsync(int id, int actor, CancellationToken ct)
    {
        if (await db.Set<EventoPlan>().Where(e => e.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new BusinessRuleException("El evento no existe.", BusinessError.NotFound);

        await auditoria.RegistrarAsync("EliminarEventoPlan", true, actor, entidad: "EventoPlan", idEntidadAfectada: id.ToString());
    }

    // incorpora al calendario las temporadas sugeridas que aun no esten en ese anio
    public async Task<object> CargarTemporadasAsync(int anio, int actor, CancellationToken ct)
    {
        if (anio is < 2020 or > 2100)
            throw new BusinessRuleException("Selecciona un año válido.");

        List<EventoPlan> nuevas;

        for (var intento = 1; ; intento++)
        {
            var cargadas = (await db.Set<EventoPlan>()
                    .Where(e => e.Anio == anio && e.Temporada != null)
                    .Select(e => e.Temporada!)
                    .ToListAsync(ct))
                .ToHashSet();

            nuevas = TemporadasBolivia.Where(t => !cargadas.Contains(t.Clave)).Select(t => new EventoPlan {
                Anio = anio,
                Mes = t.Mes,
                Titulo = t.Titulo,
                Tipo = "temporada",
                Descripcion = t.Recomendacion,
                Temporada = t.Clave,
                CreadoPor = actor
            }).ToList();

            db.Set<EventoPlan>().AddRange(nuevas);

            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            // carga simultanea del mismo anio: se descartan las filas pendientes y se reintenta
            catch (DbUpdateException error) when (error.EsDuplicado() && intento < 3)
            {
                foreach (var e in nuevas)
                    db.Set<EventoPlan>().Entry(e).State = EntityState.Detached;
            }
        }

        await auditoria.RegistrarAsync("CargarTemporadasPlan", true, actor, entidad: "EventoPlan", detalles: new {
            anio,
            cargadas = nuevas.Count
        });

        return new {
            cargadas = nuevas.Count
        };
    }
}
