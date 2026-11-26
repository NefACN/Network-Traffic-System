using Microsoft.EntityFrameworkCore;
using proyect.domain.Entidades;

namespace proyect.infrastructure.Persistence;

internal static class HttpAnalysisConfiguration
{
    public static void ConfigureHttpAnalysis(this ModelBuilder b)
    {
        b.Entity<HttpDataset>(e =>
        {
            e.ToTable("http_datasets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Origin).HasMaxLength(300);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.State).HasMaxLength(24);
            e.Property(x => x.Source).HasMaxLength(16);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<MlModel>(e =>
        {
            e.ToTable("ml_modelos");
            e.HasKey(x => x.Version);
            e.Property(x => x.Version).HasMaxLength(120);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        b.Entity<MlExecution>(e =>
        {
            e.ToTable("ml_ejecuciones");
            e.HasKey(x => x.Id);
            e.HasOne<HttpDataset>()
                .WithMany()
                .HasForeignKey(x => x.DatasetId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MlModel>()
                .WithMany()
                .HasForeignKey(x => x.ModelVersion)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Kind).HasMaxLength(24);
            e.Property(x => x.State).HasMaxLength(24);
            e.Property(x => x.Profile).HasMaxLength(64);
            e.Property(x => x.ModelVersion).HasMaxLength(120);
            e.Property(x => x.ModelMetadata).HasColumnType("jsonb");
            e.Property(x => x.Reasons).HasColumnType("jsonb");
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => new { x.State, x.LeaseUntil });
            e.HasIndex(x => new { x.DatasetId, x.CreatedAt });
        });

        b.Entity<HttpAnalysisResult>(e =>
        {
            e.ToTable("http_resultados");
            e.HasKey(x => x.Id);
            e.HasOne<MlExecution>()
                .WithMany()
                .HasForeignKey(x => x.ExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Subject).HasMaxLength(64);
            e.Property(x => x.Route).HasMaxLength(2048);
            e.Property(x => x.Method).HasMaxLength(32);
            e.Property(x => x.Features).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ExecutionId, x.Line }).IsUnique();
            e.HasIndex(x => new { x.ExecutionId, x.OccurredAt });
            e.HasIndex(x => new { x.ExecutionId, x.Cluster });
            e.HasIndex(x => new { x.ExecutionId, x.Noise, x.Candidate });
            e.HasIndex(x => new { x.ExecutionId, x.Subject });
        });

        b.Entity<HttpAnalysisReview>(e =>
        {
            e.ToTable("http_revisiones");
            e.HasKey(x => x.ResultId);
            e.HasOne<HttpAnalysisResult>()
                .WithOne()
                .HasForeignKey<HttpAnalysisReview>(x => x.ResultId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.AnalystId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.State).HasMaxLength(24);
            e.Property(x => x.Notes).HasMaxLength(2000);
        });
    }
}
