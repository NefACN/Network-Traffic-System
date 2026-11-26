using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class PreventiveHistoryConfiguration
{
    public static void ConfigurePreventiveHistory(this ModelBuilder b)
    {
        b.Entity<EnvioSimulacion>(e =>
        {
            e.Property(x => x.FechaReporte).HasColumnName("fecha_reporte");
            e.Property(x => x.ReporteSinClickPrevio).HasColumnName("reporte_sin_click_previo").HasDefaultValue(false);
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursalRegistro)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AreaOperativa>()
                .WithMany()
                .HasForeignKey(x => x.IdAreaRegistro)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CategoriaPregunta>()
                .WithMany()
                .HasForeignKey(x => x.IdCategoriaRegistro)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.EstadoEntrega).HasMaxLength(24).HasDefaultValue("legacy");
            e.HasIndex(x => new { x.IdSucursalRegistro, x.IdAreaRegistro, x.FechaEnvio });
        });

        b.Entity<IntentoCursoVoluntario>(e =>
        {
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursalRegistro)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AreaOperativa>()
                .WithMany()
                .HasForeignKey(x => x.IdAreaRegistro)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IdSucursalRegistro, x.IdAreaRegistro, x.FechaInicio });
        });

        b.Entity<CampaniaSimulacion>()
            .HasOne<Sucursal>()
            .WithMany()
            .HasForeignKey(x => x.IdSucursalObjetivo)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
