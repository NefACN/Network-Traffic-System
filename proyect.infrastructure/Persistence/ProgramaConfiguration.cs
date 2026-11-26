using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class ProgramaConfiguration
{
    public static void ConfigurePrograma(this ModelBuilder b)
    {
        b.Entity<RutaAprendizaje>(e =>
        {
            e.ToTable("rutas_aprendizaje");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Descripcion).HasMaxLength(500);
            e.Property(x => x.FechaCreacion).HasColumnType("timestamp with time zone");
            e.HasOne<AreaOperativa>()
                .WithMany()
                .HasForeignKey(x => x.IdArea)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RutaRequisito>(e =>
        {
            e.ToTable("rutas_requisitos", t => t.HasCheckConstraint(
                "CK_requisitos_plazos",
                "\"PlazoDias\" BETWEEN 1 AND 365 AND \"FrecuenciaDias\" BETWEEN 0 AND 730"));
            e.HasKey(x => x.Id);
            e.Property(x => x.VigenteDesde).HasColumnType("timestamp with time zone");
            e.HasIndex(x => new { x.IdRuta, x.IdActividad }).IsUnique();
            e.HasOne(x => x.Ruta)
                .WithMany(r => r.Requisitos)
                .HasForeignKey(x => x.IdRuta)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Actividad)
                .WithMany()
                .HasForeignKey(x => x.IdActividad)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RecordatorioAprendizaje>(e =>
        {
            e.ToTable("recordatorios_aprendizaje");
            e.HasKey(x => x.Id);
            e.Property(x => x.Fecha).HasColumnType("timestamp with time zone");
            e.HasIndex(x => new { x.IdUsuario, x.Fecha });
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Constancia>(e =>
        {
            e.ToTable("constancias");
            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => x.IdIntento).IsUnique();
            e.Property(x => x.NombreCompleto).HasMaxLength(200);
            e.Property(x => x.Actividad).HasMaxLength(150);
            e.Property(x => x.Tema).HasMaxLength(100);
            e.Property(x => x.Porcentaje).HasPrecision(5, 1);
            e.Property(x => x.FechaAprobacion).HasColumnType("timestamp with time zone");
            e.Property(x => x.FechaEmision).HasColumnType("timestamp with time zone");
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IntentoCursoVoluntario>()
                .WithMany()
                .HasForeignKey(x => x.IdIntento)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EventoPlan>(e =>
        {
            e.ToTable("plan_eventos", t =>
            {
                t.HasCheckConstraint("CK_plan_mes", "\"Mes\" BETWEEN 1 AND 12 AND \"Anio\" BETWEEN 2020 AND 2100");
                t.HasCheckConstraint("CK_plan_tipo", "\"Tipo\" IN ('campania','capacitacion','encuesta','comunicacion','temporada')");
                t.HasCheckConstraint("CK_plan_estado", "\"Estado\" IN ('planificado','realizado','cancelado')");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Titulo).HasMaxLength(150);
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Temporada).HasMaxLength(40);
            e.Property(x => x.Descripcion).HasMaxLength(1000);
            e.Property(x => x.FechaCreacion).HasColumnType("timestamp with time zone");
            e.HasIndex(x => new { x.Anio, x.Mes });
            e.HasIndex(x => new { x.Anio, x.Temporada }).IsUnique().HasFilter("\"Temporada\" IS NOT NULL");
            e.HasOne<CategoriaPregunta>()
                .WithMany()
                .HasForeignKey(x => x.IdCategoria)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursal)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CampaniaSimulacion>()
                .WithMany()
                .HasForeignKey(x => x.IdCampania)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CreadoPor)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<EncuestaCultura>(e =>
        {
            e.ToTable("encuestas_cultura", t => t.HasCheckConstraint(
                "CK_encuestas_estado",
                "\"Estado\" IN ('borrador','abierta','cerrada')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Titulo).HasMaxLength(150);
            e.Property(x => x.Periodo).HasMaxLength(20);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Preguntas).HasColumnType("jsonb");
            foreach (var p in new[] {
                nameof(EncuestaCultura.FechaInicio),
                nameof(EncuestaCultura.FechaFin),
                nameof(EncuestaCultura.FechaCreacion)
            })
                e.Property(p).HasColumnType("timestamp with time zone");
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CreadoPor)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ParticipacionEncuesta>(e =>
        {
            e.ToTable("participaciones_encuesta");
            e.HasKey(x => new { x.IdEncuesta, x.IdUsuario });
            e.HasOne<EncuestaCultura>()
                .WithMany()
                .HasForeignKey(x => x.IdEncuesta)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RespuestaEncuesta>(e =>
        {
            e.ToTable("respuestas_encuesta");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Respuestas).HasColumnType("jsonb");
            e.HasIndex(x => x.IdEncuesta);
            e.HasOne<EncuestaCultura>()
                .WithMany()
                .HasForeignKey(x => x.IdEncuesta)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursal)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AreaOperativa>()
                .WithMany()
                .HasForeignKey(x => x.IdArea)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
