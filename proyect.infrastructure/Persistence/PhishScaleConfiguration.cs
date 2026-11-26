using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class PhishScaleConfiguration
{
    public static void ConfigurePhishScale(this ModelBuilder b)
    {
        b.Entity<PlantillaPhishing>(e =>
        {
            e.Property(x => x.AlineacionGeneral)
                .HasColumnName("alineacion_general")
                .HasMaxLength(10)
                .HasDefaultValue("media");
            e.Property(x => x.PaginaSenuelo)
                .HasColumnName("pagina_senuelo")
                .HasMaxLength(20)
                .HasDefaultValue("ninguna");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_plantillas_alineacion_general", "alineacion_general IN ('debil','media','fuerte')");
                t.HasCheckConstraint("CK_plantillas_pagina_senuelo", "pagina_senuelo IN ('ninguna','credenciales')");
            });
        });

        b.Entity<SenalAlertaPlantilla>(e =>
        {
            e.ToTable("senales_alerta_plantilla", t =>
                t.HasCheckConstraint("CK_senales_tipo", "tipo IN ('errores','tecnico','visual','lenguaje','tacticas')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id_senal");
            e.Property(x => x.IdPlantilla).HasColumnName("id_plantilla");
            e.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(20).IsRequired();
            e.Property(x => x.Descripcion).HasColumnName("descripcion").HasMaxLength(300).IsRequired();
            e.Property(x => x.Fragmento).HasColumnName("fragmento").HasMaxLength(500);
            e.Property(x => x.Orden).HasColumnName("orden");
            e.HasOne(x => x.Plantilla)
                .WithMany(p => p.Senales)
                .HasForeignKey(x => x.IdPlantilla)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.IdPlantilla, x.Orden });
        });

        b.Entity<AlineacionPlantillaArea>(e =>
        {
            e.ToTable("alineaciones_plantilla_area", t =>
                t.HasCheckConstraint("CK_alineaciones_nivel", "nivel IN ('debil','media','fuerte')"));
            e.HasKey(x => new { x.IdPlantilla, x.IdArea });
            e.Property(x => x.IdPlantilla).HasColumnName("id_plantilla");
            e.Property(x => x.IdArea).HasColumnName("id_area");
            e.Property(x => x.Nivel).HasColumnName("nivel").HasMaxLength(10).IsRequired();
            e.HasOne(x => x.Plantilla)
                .WithMany(p => p.Alineaciones)
                .HasForeignKey(x => x.IdPlantilla)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Area)
                .WithMany()
                .HasForeignKey(x => x.IdArea)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CampaniaSimulacion>(e =>
        {
            e.Property(x => x.PhishScaleRegistro).HasColumnName("phish_scale_registro").HasColumnType("jsonb");
            e.Property(x => x.PaginaSenueloRegistro).HasColumnName("pagina_senuelo_registro").HasMaxLength(20);
        });

        b.Entity<EnvioSimulacion>(e =>
        {
            e.Property(x => x.AlineacionRegistro).HasColumnName("alineacion_registro").HasMaxLength(10);
            e.Property(x => x.DificultadNistRegistro).HasColumnName("dificultad_nist_registro").HasMaxLength(20);
            e.Property(x => x.IngresoCredenciales).HasColumnName("ingreso_credenciales").HasDefaultValue(false);
            e.Property(x => x.FechaIngresoCredenciales)
                .HasColumnName("fecha_ingreso_credenciales")
                .HasColumnType("timestamp with time zone");
        });
    }
}
