using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class CicloHttpConfiguration
{
    public static void ConfigureCicloHttp(this ModelBuilder b)
    {
        b.Entity<InteraccionSimulacionHttp>(e =>
        {
            e.ToTable("interacciones_simulacion_http", t =>
                t.HasCheckConstraint("CK_interacciones_http_tipo", "\"Tipo\" IN ('apertura','clic','credenciales')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Fecha).HasColumnType("timestamp with time zone");
            e.Property(x => x.Ip).HasMaxLength(15);
            e.Property(x => x.Metodo).HasMaxLength(10);
            e.Property(x => x.Ruta).HasMaxLength(300);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.Referer).HasMaxLength(300);
            e.HasIndex(x => new { x.IdCampania, x.Fecha });
            e.HasIndex(x => x.IdEnvio);
            e.HasOne<EnvioSimulacion>()
                .WithMany()
                .HasForeignKey(x => x.IdEnvio)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CampaniaSimulacion>()
                .WithMany()
                .HasForeignKey(x => x.IdCampania)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<LineaSimulacionHttp>(e =>
        {
            e.ToTable("lineas_simulacion_http");
            e.HasKey(x => new { x.DatasetId, x.Linea });
            e.HasOne<HttpDataset>()
                .WithMany()
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<InteraccionSimulacionHttp>()
                .WithMany()
                .HasForeignKey(x => x.IdInteraccion)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<HttpDataset>(e =>
        {
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursal)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CampaniaSimulacion>()
                .WithMany()
                .HasForeignKey(x => x.IdCampaniaSimulacion)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SugerenciaConcientizacion>(e =>
        {
            e.ToTable("sugerencias_concientizacion", t =>
            {
                t.HasCheckConstraint("CK_sugerencias_origen", "\"Origen\" IN ('http','incidente')");
                t.HasCheckConstraint("CK_sugerencias_estado", "\"Estado\" IN ('pendiente','campania','microleccion','descartada')");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Origen).HasMaxLength(20);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Motivo).HasMaxLength(500);
            e.Property(x => x.Evidencias).HasColumnType("jsonb");
            e.Property(x => x.Nota).HasMaxLength(500);
            foreach (var p in new[] {
                nameof(SugerenciaConcientizacion.FechaCreacion),
                nameof(SugerenciaConcientizacion.UltimaEvidencia),
                nameof(SugerenciaConcientizacion.FechaAtencion)
            })
                e.Property(p).HasColumnType("timestamp with time zone");
            e.HasIndex(x => new { x.Estado, x.IdSucursal, x.IdArea });
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursal)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AreaOperativa>()
                .WithMany()
                .HasForeignKey(x => x.IdArea)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CategoriaPregunta>()
                .WithMany()
                .HasForeignKey(x => x.IdCategoria)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ActividadEducativa>()
                .WithMany()
                .HasForeignKey(x => x.IdActividad)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.AtendidaPor)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
