using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class GobernanzaConfiguration
{
    // pretextos que explotan temas sensibles (NIST SP 800-50 Rev. 1)
    public const string PretextosIniciales = "[\"bono\",\"bonificación\",\"aguinaldo\",\"aumento de sueldo\",\"despido\",\"desvinculación\",\"salud\",\"enfermedad\",\"diagnóstico\",\"covid\",\"vacuna\",\"fallecimiento\",\"duelo\"]";

    public static void ConfigureGobernanza(this ModelBuilder b)
    {
        b.Entity<PoliticaGobernanza>(e =>
        {
            e.ToTable("politica_gobernanza", t =>
            {
                t.HasCheckConstraint(
                    "CK_gobernanza_horas",
                    "\"HoraInicio\" BETWEEN 0 AND 23 AND \"HoraFin\" BETWEEN 1 AND 24 AND \"HoraInicio\" < \"HoraFin\"");
                t.HasCheckConstraint("CK_gobernanza_limites", "\"MaxSimulacionesPorPersona\" >= 0 AND \"UmbralAgregacion\" >= 1");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.PretextosProhibidos).HasColumnType("jsonb");
            e.Property(x => x.ActualizadaEn).HasColumnType("timestamp with time zone");
            e.HasData(new PoliticaGobernanza { PretextosProhibidos = PretextosIniciales });
        });

        b.Entity<ExclusionSimulacion>(e =>
        {
            e.ToTable("exclusiones_simulacion", t =>
                t.HasCheckConstraint(
                    "CK_exclusiones_motivo",
                    "\"Motivo\" IN ('licencia','vacaciones','incidente_reciente','solicitud','otro')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Motivo).HasMaxLength(30);
            e.Property(x => x.Nota).HasMaxLength(200);
            e.Property(x => x.Desde).HasColumnType("timestamp with time zone");
            e.Property(x => x.Hasta).HasColumnType("timestamp with time zone");
            e.Property(x => x.FechaCreacion).HasColumnType("timestamp with time zone");
            e.HasIndex(x => new { x.IdUsuario, x.Activa });
            e.HasOne(x => x.Usuario)
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CreadoPor)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<CampaniaSimulacion>(e =>
        {
            e.Property(x => x.EstadoAprobacion)
                .HasColumnName("estado_aprobacion")
                .HasMaxLength(20)
                .HasDefaultValue("borrador");
            e.Property(x => x.SolicitadaPor).HasColumnName("solicitada_por");
            e.Property(x => x.FechaSolicitud)
                .HasColumnName("fecha_solicitud")
                .HasColumnType("timestamp with time zone");
            e.Property(x => x.RevisadaPor).HasColumnName("revisada_por");
            e.Property(x => x.FechaRevision).HasColumnName("fecha_revision").HasColumnType("timestamp with time zone");
            e.Property(x => x.ComentarioRevision).HasColumnName("comentario_revision").HasMaxLength(500);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.SolicitadaPor)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.RevisadaPor)
                .OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_campanias_estado_aprobacion",
                "estado_aprobacion IN ('borrador','pendiente','aprobada','rechazada')"));
        });

        b.Entity<IncidenteReportado>(e =>
        {
            e.ToTable("incidentes_reportados", t =>
            {
                t.HasCheckConstraint("CK_incidentes_canal", "\"Canal\" IN ('correo','sms','whatsapp','llamada','otro')");
                t.HasCheckConstraint("CK_incidentes_interaccion", "\"Interaccion\" IN ('ninguna','clic','adjunto','datos')");
                t.HasCheckConstraint("CK_incidentes_prioridad", "\"Prioridad\" IN ('baja','media','alta')");
                t.HasCheckConstraint("CK_incidentes_estado", "\"Estado\" IN ('recibido','en_analisis','resuelto')");
                t.HasCheckConstraint(
                    "CK_incidentes_clasificacion",
                    "\"Clasificacion\" IS NULL OR \"Clasificacion\" IN ('malicioso','spam','legitimo','simulacion')");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Codigo).HasMaxLength(20);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Canal).HasMaxLength(20);
            e.Property(x => x.Remitente).HasMaxLength(200);
            e.Property(x => x.Asunto).HasMaxLength(200);
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.Interaccion).HasMaxLength(20);
            e.Property(x => x.Prioridad).HasMaxLength(10);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Clasificacion).HasMaxLength(20);
            e.Property(x => x.Respuesta).HasMaxLength(2000);
            foreach (var p in new[] {
                nameof(IncidenteReportado.FechaCreacion),
                nameof(IncidenteReportado.FechaAtencion),
                nameof(IncidenteReportado.FechaResolucion)
            })
                e.Property(p).HasColumnType("timestamp with time zone");
            e.HasIndex(x => new { x.Estado, x.Prioridad, x.FechaCreacion });
            e.HasIndex(x => new { x.IdUsuario, x.FechaCreacion });
            e.HasOne(x => x.Usuario)
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdAnalista)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Sucursal>()
                .WithMany()
                .HasForeignKey(x => x.IdSucursalRegistro)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AreaOperativa>()
                .WithMany()
                .HasForeignKey(x => x.IdAreaRegistro)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<IncidenteSeguimiento>(e =>
        {
            e.ToTable("incidentes_seguimiento");
            e.HasKey(x => x.Id);
            e.Property(x => x.EstadoAnterior).HasMaxLength(20);
            e.Property(x => x.EstadoNuevo).HasMaxLength(20);
            e.Property(x => x.Nota).HasMaxLength(2000);
            e.Property(x => x.Fecha).HasColumnType("timestamp with time zone");
            e.HasOne(x => x.Incidente)
                .WithMany(i => i.Seguimientos)
                .HasForeignKey(x => x.IdIncidente)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
