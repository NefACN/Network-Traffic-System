using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class AprendizajeConfiguration
{
    public static void ConfigureAprendizaje(this ModelBuilder b)
    {
        b.Entity<ActividadEducativa>(e =>
        {
            e.ToTable("actividades_educativas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Titulo).HasMaxLength(150);
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.Secciones).HasColumnType("jsonb");
            e.Property(x => x.PreguntasPublicadas).HasColumnType("jsonb");
            e.HasOne(x => x.Categoria)
                .WithMany()
                .HasForeignKey(x => x.IdCategoria)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActividadPregunta>(e =>
        {
            e.ToTable("actividad_preguntas");
            e.HasKey(x => new { x.IdActividad, x.IdPregunta });
            e.HasOne(x => x.Actividad)
                .WithMany(x => x.Preguntas)
                .HasForeignKey(x => x.IdActividad)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Pregunta)
                .WithMany()
                .HasForeignKey(x => x.IdPregunta)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ReglasGamificacion>(e => { e.ToTable("reglas_gamificacion"); e.HasKey(x => x.Id); e.HasData(new ReglasGamificacion()); });
        b.Entity<MovimientoPuntos>(e =>
        {
            e.ToTable("movimientos_puntos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Clave).HasMaxLength(120);
            e.Property(x => x.Concepto).HasMaxLength(200);
            e.HasIndex(x => new { x.IdUsuario, x.Clave }).IsUnique();
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<IntentoCursoVoluntario>(e =>
        {
            e.HasOne<ActividadEducativa>()
                .WithMany()
                .HasForeignKey(x => x.IdActividad)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TituloRegistro).HasMaxLength(150);
            e.Property(x => x.ContenidoRegistro).HasColumnType("jsonb");
            e.Property(x => x.CuestionarioRegistro).HasColumnType("jsonb");
            e.Property(x => x.RespuestasRegistro).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Id_Usuario, x.IdActividad })
                .IsUnique()
                .HasFilter("\"IdActividad\" IS NOT NULL AND fecha_completado IS NULL");
        });

        b.Entity<Insignia>(e => { e.Property(x => x.TipoCondicion).HasMaxLength(30); e.Property(x => x.Activa).HasDefaultValue(true); });
    }
}
