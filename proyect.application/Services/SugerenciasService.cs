using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;
using proyect.domain.Constants;
using proyect.domain.Entidades;

namespace proyect.application.Services;

// sugiere campanias o microlecciones desde analisis HTTP e incidentes, nunca sin decision humana
public sealed class SugerenciasService(IApplicationDbContext db, IAuditoriaService auditoria)
{
    public const int DiasAgrupacion = 30, MaxEvidencias = 20;
    public sealed record Evidencia(string Referencia, string Descripcion, DateTime Fecha);

    // agrupa evidencias de la misma sucursal y area en una sugerencia pendiente reciente
    public async Task<int> SugerirAsync(string origen, int? sucursal, int? area, int? categoria, string referencia, string descripcion, CancellationToken ct = default)
    {
        // candado/  evidencias simultaneas del mismo alcance se agrupan en una sola sugerencia
        await using var propia = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.BloquearAsync(Bloqueos.Sugerencias, 0, ct);
        var id = await AgruparAsync(origen, sucursal, area, categoria, referencia, descripcion, ct);
        if (propia is not null)
            await propia.CommitAsync(ct);
        return id;
    }

    private async Task<int> AgruparAsync(string origen, int? sucursal, int? area, int? categoria, string referencia, string descripcion, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var limite = now.AddDays(-DiasAgrupacion);
        var evidencia = new Evidencia(referencia, descripcion.Length > 300 ? descripcion[..300] : descripcion, now);
        var existente = await db.Set<SugerenciaConcientizacion>().Where(s => s.Estado == "pendiente" && s.IdSucursal == sucursal
            && s.IdArea == area && s.UltimaEvidencia >= limite).OrderByDescending(s => s.UltimaEvidencia).FirstOrDefaultAsync(ct);
        if (existente is not null)
        {
            var lista = JsonSerializer.Deserialize<List<Evidencia>>(existente.Evidencias) ?? [];
            if (lista.Any(e => e.Referencia == referencia))
                return existente.Id;
            lista.Insert(0, evidencia);
            existente.Evidencias = JsonSerializer.Serialize(lista.Take(MaxEvidencias));
            existente.CantidadEvidencias++;
            existente.UltimaEvidencia = now;
            existente.IdCategoria ??= categoria;
            await db.SaveChangesAsync(ct);
            return existente.Id;
        }
        var s = new SugerenciaConcientizacion {
            Origen = origen,
            IdSucursal = sucursal,
            IdArea = area,
            IdCategoria = categoria,
            Motivo = evidencia.Descripcion,
            Evidencias = JsonSerializer.Serialize(new[] { evidencia }),
            FechaCreacion = now,
            UltimaEvidencia = now,
        };
        db.Set<SugerenciaConcientizacion>().Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    // colaboradores activos del alcance, sin exclusiones vigentes
    private IQueryable<ApplicationUser> Destinatarios(SugerenciaConcientizacion s)
    {
        var now = DateTime.UtcNow;
        return db.Users.Where(u => u.Activo && (s.IdSucursal == null || u.Id_Sucursal == s.IdSucursal) && (s.IdArea == null || u.Id_Area == s.IdArea)
            && db.RolUsuario.Any(r => r.IdUsuario == u.Id && r.Activo && r.Rol.Nombre == RolesSistemas.RolColaborador && r.FechaInicio <= now && (r.FechaFin == null || r.FechaFin > now))
            && !db.Set<ExclusionSimulacion>().Any(x => x.IdUsuario == u.Id && x.Activa && x.Desde <= now && (x.Hasta == null || x.Hasta > now)));
    }

    // sugerencias paginadas
    public async Task<object> ListarAsync(string? estado, CancellationToken ct, int? pagina = null, int? tamanio = null)
    {
        var q = db.Set<SugerenciaConcientizacion>().AsNoTracking();
        if (!string.IsNullOrEmpty(estado))
            q = q.Where(s => s.Estado == estado);
        var paginaFilas = await q.OrderBy(s => s.Estado != "pendiente").ThenByDescending(s => s.UltimaEvidencia).ThenByDescending(s => s.Id).PaginarAsync(pagina, tamanio, ct);
        var filas = paginaFilas.Items;
        var sucursales = await db.Sucursales.AsNoTracking().ToDictionaryAsync(s => s.Id_Sucursal, s => s.Nombre, ct);
        var areas = await db.AreasOperativas.AsNoTracking().ToDictionaryAsync(a => a.Id_Area, a => a.Nombre, ct);
        var temas = await db.CategoriasPregunta.AsNoTracking().ToDictionaryAsync(c => c.IdCategoria, c => c.Nombre, ct);
        var resultado = new List<object>();
        foreach (var s in filas)
            resultado.Add(new {
                s.Id,
                s.Origen,
                s.IdSucursal,
                sucursal = s.IdSucursal is { } b ? sucursales.GetValueOrDefault(b) : null,
                s.IdArea,
                area = s.IdArea is { } a ? areas.GetValueOrDefault(a) : null,
                s.IdCategoria,
                tema = s.IdCategoria is { } c ? temas.GetValueOrDefault(c) : null,
                s.Motivo,
                evidencias = JsonSerializer.Deserialize<List<Evidencia>>(s.Evidencias),
                s.CantidadEvidencias,
                s.FechaCreacion,
                s.UltimaEvidencia,
                s.Estado,
                s.FechaAtencion,
                s.Nota,
                s.IdActividad,
                s.PersonasAsignadas,
                alcance = s.Estado == "pendiente" ? await Destinatarios(s).CountAsync(ct) : (int?)null,
            });
        return new ResultadoPaginado<object>(resultado, paginaFilas.Total, paginaFilas.Pagina, paginaFilas.Tamanio);
    }

    private async Task<SugerenciaConcientizacion> PendienteAsync(int id, CancellationToken ct)
    {
        var s = await db.Set<SugerenciaConcientizacion>().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new BusinessRuleException("La sugerencia no existe.", BusinessError.NotFound);
        if (s.Estado != "pendiente")
            throw new BusinessRuleException("La sugerencia ya fue atendida.", BusinessError.Conflict);
        return s;
    }

    public async Task<object> PrepararCampaniaAsync(int id, int actor, CancellationToken ct)
    {
        var s = await PendienteAsync(id, ct);
        s.Estado = "campania";
        s.AtendidaPor = actor;
        s.FechaAtencion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("AtenderSugerencia", true, actor, entidad: "SugerenciaConcientizacion", idEntidadAfectada: id.ToString(), detalles: new { accion = "campania" });
        return new { s.IdSucursal, s.IdArea };
    }

    public async Task<object> AsignarMicroleccionAsync(int id, int idActividad, int actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = await PendienteAsync(id, ct);
        if (!await db.Set<ActividadEducativa>().AnyAsync(a => a.Id == idActividad && a.Habilitada && a.EsMicroleccion, ct))
            throw new BusinessRuleException("Selecciona una microlección habilitada.");

        var personas = await Destinatarios(s).Select(u => u.Id).ToListAsync(ct);

        if (personas.Count == 0)
            throw new BusinessRuleException("No hay colaboradores activos en el alcance de la sugerencia.");

        var now = DateTime.UtcNow;

        foreach (var persona in personas)
            db.Set<AsignacionMicroleccion>().Add(new AsignacionMicroleccion {
                IdUsuario = persona,
                IdSugerencia = id,
                IdActividad = idActividad,
                Motivo = "sugerencia",
                FechaAsignacion = now
            });

        s.Estado = "microleccion";
        s.IdActividad = idActividad;
        s.PersonasAsignadas = personas.Count;
        s.AtendidaPor = actor;
        s.FechaAtencion = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        // otra solicitud simultanea ya atendio la sugerencia, se responde como un reintento
        catch (DbUpdateException error) when (error.EsDuplicado())
        {
            throw new BusinessRuleException("La sugerencia ya fue atendida.", BusinessError.Conflict);
        }

        await auditoria.RegistrarAsync("AtenderSugerencia", true, actor, entidad: "SugerenciaConcientizacion", idEntidadAfectada: id.ToString(),
            detalles: new { accion = "microleccion", idActividad, personas = personas.Count });
        await tx.CommitAsync(ct);

        return new { asignadas = personas.Count };
    }

    public async Task DescartarAsync(int id, string? nota, int actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(nota))
            throw new BusinessRuleException("Indica por qué se descarta la sugerencia.");

        var s = await PendienteAsync(id, ct);
        s.Estado = "descartada";
        s.Nota = nota.Trim()[..Math.Min(500, nota.Trim().Length)];
        s.AtendidaPor = actor;
        s.FechaAtencion = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await auditoria.RegistrarAsync("AtenderSugerencia", true, actor, entidad: "SugerenciaConcientizacion", idEntidadAfectada: id.ToString(), detalles: new { accion = "descartada" });
    }
}
