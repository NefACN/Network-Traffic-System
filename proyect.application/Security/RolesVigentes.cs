using Microsoft.EntityFrameworkCore;
using proyect.domain.Constants;
using proyect.domain.Entidades;
using System;
using System.Collections.Generic;
using System.Text;

namespace proyect.application.Security
{
    public static class RolesVigentes
    {
        public static IQueryable<RolUsuario> Vigentes(
            this IQueryable<RolUsuario> asignaciones,
            DateTime ahora) => asignaciones.Where(
                r => r.Activo && r.Rol.Activo && r.FechaInicio <= ahora &&
                (r.FechaFin == null || r.FechaFin > ahora));

        public static async Task<string> ActorEfectivoAsync(this IQueryable<RolUsuario> asignaciones,
            int idUsuario, DateTime ahora, CancellationToken ct = default)
        {
            var nombres = await asignaciones.Vigentes(ahora)
                .Where(r => r.IdUsuario == idUsuario)
                .Select(r => r.Rol.Nombre)
                .ToListAsync(ct);
            return RolesSistemas.ActorEfectivo(nombres);
        }
    }
}
