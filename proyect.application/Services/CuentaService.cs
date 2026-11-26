using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Exceptions;

namespace proyect.application.Services;

// datos del usuario autenticado
public sealed class CuentaService(IApplicationDbContext db)
{
    private static readonly string[] AccionesCuenta = ["Login", "LoginFallido", "CambioPassword", "Logout"];

    public sealed record EventoCuenta(string Accion, DateTime FechaHora, string? IpOrigen, bool Exitoso);

    public sealed record PerfilCuenta(int Id, string Nombre, string Apellido, string? Email, string? UserName,
        IReadOnlyList<string> Roles, string? Sucursal, string? Area, DateTime FechaCreacion,
        DateTime? UltimoAcceso, IReadOnlyList<EventoCuenta> ActividadReciente);

    public async Task<PerfilCuenta> PerfilAsync(int usuarioId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var usuario = await db.Users.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => new {
            u.Id,
            u.Nombre,
            u.Apellido,
            u.Email,
            u.UserName,
            u.FechaCreacion,
            sucursal = u.Sucursal != null ? u.Sucursal.Nombre : null,
            area = u.AreaOperativa != null ? u.AreaOperativa.Nombre : null,
        }).FirstOrDefaultAsync(ct) ?? throw new BusinessRuleException("No se encontro la cuenta.", BusinessError.NotFound);

        var roles = await db.RolUsuario.AsNoTracking()
            .Where(r => r.IdUsuario == usuarioId && r.Activo && r.Rol.Activo && (r.FechaFin == null || r.FechaFin > now))
            .Select(r => r.Rol.Nombre).Distinct().ToListAsync(ct);

        var actividad = await db.RegistrosAuditoria.AsNoTracking()
            .Where(a => a.IdUsuario == usuarioId && AccionesCuenta.Contains(a.Accion))
            .OrderByDescending(a => a.FechaHora).Take(6)
            .Select(a => new EventoCuenta(a.Accion, a.FechaHora, a.IpOrigen, a.Exitoso)).ToListAsync(ct);

        // ultimo acceso exitoso, los intentos fallidos se consultan aparte
        var ultimoAcceso = await db.RegistrosAuditoria.AsNoTracking()
            .Where(a => a.IdUsuario == usuarioId && a.Accion == "Login" && a.Exitoso)
            .OrderByDescending(a => a.FechaHora)
            .Skip(1)
            .Select(a => (DateTime?)a.FechaHora)
            .FirstOrDefaultAsync(ct);

        return new PerfilCuenta(
            usuario.Id,
            usuario.Nombre,
            usuario.Apellido,
            usuario.Email,
            usuario.UserName,
            roles,
            usuario.sucursal,
            usuario.area,
            usuario.FechaCreacion,
            ultimoAcceso,
            actividad);
    }
}
