using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;

namespace proyect.application.Services;

public sealed class AuditoriaConsultaService(IApplicationDbContext db)
{
    private IQueryable<proyect.domain.Entidades.RegistroAuditoria> Consulta(int? usuario, string? accion, DateTime? desde, DateTime? hasta)
    {
        desde = HoraBolivia.DesdeUtc(desde);
        hasta = HoraBolivia.HastaUtc(hasta);
        if (desde > hasta)
            throw new BusinessRuleException("El inicio del periodo no puede ser posterior al final.");

        var q = db.RegistrosAuditoria.AsNoTracking();

        if (usuario.HasValue)
            q = q.Where(r => r.IdUsuario == usuario);

        if (!string.IsNullOrWhiteSpace(accion))
            q = q.Where(r => r.Accion == accion);

        if (desde.HasValue)
            q = q.Where(r => r.FechaHora >= desde);

        if (hasta.HasValue)
            q = q.Where(r => r.FechaHora <= hasta);

        return q;
    }
    public async Task<object> PaginarAsync(int? usuario, string? accion, DateTime? desde, DateTime? hasta, int pagina, int tamanio, CancellationToken ct)
    {
        var items = await ListarAsync(usuario, accion, desde, hasta, pagina, tamanio, ct);

        return new {
            pagina,
            tamanio = Math.Clamp(tamanio, 1, 200),
            total = await Consulta(usuario, accion, desde, hasta).CountAsync(ct),
            items
        };
    }
    public async Task<RegistroAuditoriaDto> DetalleAsync(long id, CancellationToken ct) => await db.RegistrosAuditoria
        .AsNoTracking()
        .Where(r => r.Id_Auditoria == id)
        .Select(r => new RegistroAuditoriaDto(
            r.Id_Auditoria,
            r.FechaHora,
            r.IdUsuario,
            r.NombreUsuario,
            r.Accion,
            r.Entidad,
            r.IdEntidadAfectada,
            r.Exitoso,
            r.IpOrigen,
            r.Detalles)).SingleOrDefaultAsync(ct)
        ?? throw new BusinessRuleException("La operación no existe.", BusinessError.NotFound);

    public async Task<object> CatalogosAsync(CancellationToken ct) => new {
        usuarios = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Nombre)
            .ThenBy(u => u.Id)
            .Select(u => new { id = u.Id, nombre = u.Nombre + " " + u.Apellido, usuario = u.UserName })
            .ToListAsync(ct),

        acciones = await db.RegistrosAuditoria
            .AsNoTracking()
            .Select(r => r.Accion)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct)
    };

    public async Task<List<RegistroAuditoriaDto>> ListarAsync(int? idUsuario, string? accion, DateTime? desde, DateTime? hasta,
        int pagina, int tamanioPagina, CancellationToken ct)
    {
        tamanioPagina = Math.Clamp(tamanioPagina, 1, 200);
        if (pagina < 1 || pagina > int.MaxValue / tamanioPagina)
            throw new BusinessRuleException("La página solicitada está fuera del rango permitido.");

        var query = Consulta(idUsuario, accion, desde, hasta);

        return await query.OrderByDescending(r => r.FechaHora).ThenByDescending(r => r.Id_Auditoria)
            .Skip((pagina - 1) * tamanioPagina).Take(tamanioPagina)
            .Select(r => new RegistroAuditoriaDto(r.Id_Auditoria, r.FechaHora, r.IdUsuario, r.NombreUsuario,
                r.Accion, r.Entidad, r.IdEntidadAfectada, r.Exitoso, r.IpOrigen, r.Detalles)).ToListAsync(ct);
    }
}
