using Microsoft.EntityFrameworkCore;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;
using proyect.domain.Constants;

namespace proyect.application.Services;

public sealed class RolesService(IApplicationDbContext db)
{
    public Task<List<RolDto>> ListarAsync(bool? activo, CancellationToken ct) => db.Roles.AsNoTracking()
        .Where(r => (r.Nombre == RolesSistemas.RolAdmin || r.Nombre == RolesSistemas.RolGerente || r.Nombre == RolesSistemas.RolColaborador)
            && (!activo.HasValue || r.Activo == activo.Value)).OrderBy(r => r.Nombre)
        .Select(r => new RolDto(r.IdRol, r.Nombre, r.Descripcion, r.Activo)).ToListAsync(ct);

    public Task<RolDto> CrearAsync(CrearRolRequest request, CancellationToken ct) =>
        throw new BusinessRuleException("El sistema utiliza tres actores fijos: Admin, GerenteSucursal y Colaborador.");
    public Task ActualizarAsync(int id, ActualizarRolRequest request, CancellationToken ct) =>
        throw new BusinessRuleException("Los roles del sistema no se pueden renombrar ni modificar.");
    public Task CambiarEstadoAsync(int id, bool activo, CancellationToken ct) =>
        throw new BusinessRuleException("Los roles del sistema no se pueden desactivar. Cambia la asignación del usuario.");
}
