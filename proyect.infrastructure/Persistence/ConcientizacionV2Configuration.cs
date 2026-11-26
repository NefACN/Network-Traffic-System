using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class ConcientizacionV2Configuration
{
    public static void ConfigureConcientizacionV2(this ModelBuilder b)
    {
        b.Entity<ActividadEducativa>(e =>
        {
            e.Property(x => x.EsMicroleccion).HasDefaultValue(false);
            e.Property(x => x.ParaCredenciales).HasDefaultValue(false);
        });

        // inspeccion interactiva tras el clic: datos de ensenanza de la plantilla y resultado autoinformado
        b.Entity<PlantillaPhishing>(e =>
        {
            e.Property(x => x.RemitenteNombre).HasMaxLength(120);
            e.Property(x => x.RemitenteCorreo).HasMaxLength(254);
            e.Property(x => x.EnlaceMostrado).HasMaxLength(300);
        });

        b.Entity<EnvioSimulacion>(e =>
        {
            e.Property(x => x.FechaInspeccion).HasColumnType("timestamp with time zone");
            e.Property(x => x.InspeccionOmitidas).HasMaxLength(200);
        });

        b.Entity<AsignacionMicroleccion>(e =>
        {
            e.ToTable("asignaciones_microleccion", t =>
                t.HasCheckConstraint("CK_asignaciones_motivo", "\"Motivo\" IN ('clic','credenciales','sugerencia','critico')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Motivo).HasMaxLength(20);
            e.Property(x => x.FechaAsignacion).HasColumnType("timestamp with time zone");
            e.Property(x => x.FechaCompletado).HasColumnType("timestamp with time zone");
            // una asignacion por envio: los clics repetidos no duplican tareas
            e.HasIndex(x => x.IdEnvio).IsUnique().HasFilter("\"IdEnvio\" IS NOT NULL");
            e.HasIndex(x => new { x.IdSugerencia, x.IdUsuario }).IsUnique().HasFilter("\"IdSugerencia\" IS NOT NULL");
            e.HasOne<SugerenciaConcientizacion>()
                .WithMany()
                .HasForeignKey(x => x.IdSugerencia)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IdUsuario, x.FechaCompletado });
            // plan de refuerzo (nivel Critico): una misma microleccion pendiente no se asigna dos veces
            e.HasIndex(x => new { x.IdUsuario, x.IdActividad }).IsUnique().HasDatabaseName("IX_asignaciones_plan_critico_pendiente")
                .HasFilter("\"Motivo\" = 'critico' AND \"FechaCompletado\" IS NULL");
            e.HasOne<EnvioSimulacion>()
                .WithMany()
                .HasForeignKey(x => x.IdEnvio)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Actividad)
                .WithMany()
                .HasForeignKey(x => x.IdActividad)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IntentoCursoVoluntario>()
                .WithMany()
                .HasForeignKey(x => x.IdIntento)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PoliticaCyberScore>(e =>
        {
            e.ToTable("politica_cyberscore");
            e.HasKey(x => x.Id);
            foreach (var p in new[] {
                nameof(PoliticaCyberScore.PesoClic),
                nameof(PoliticaCyberScore.PesoCredenciales),
                nameof(PoliticaCyberScore.BonoReporte),
                nameof(PoliticaCyberScore.BonoCuestionario),
                nameof(PoliticaCyberScore.BonoMicroleccion),
                nameof(PoliticaCyberScore.FactorPocoDificil),
                nameof(PoliticaCyberScore.FactorMuyDificil),
                nameof(PoliticaCyberScore.ExposicionBaja),
                nameof(PoliticaCyberScore.ExposicionMedia),
                nameof(PoliticaCyberScore.ExposicionAlta),
                nameof(PoliticaCyberScore.UmbralBajo),
                nameof(PoliticaCyberScore.UmbralMedio),
                nameof(PoliticaCyberScore.UmbralAlto)
            })
                e.Property(p).HasPrecision(6, 2);
            e.Property(x => x.ActualizadaEn).HasColumnType("timestamp with time zone");
            e.HasData(new PoliticaCyberScore());
        });
    }
}
