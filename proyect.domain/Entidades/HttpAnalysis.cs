namespace proyect.domain.Entidades;

public sealed class HttpDataset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Origin { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string State { get; set; } = "validating";
    public string Source { get; set; } = "file";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    // sucursal de la fuente, permite dirigir sugerencias
    public int? IdSucursal { get; set; }
    // campania que origino este conjunto (trafico de verdad conocida)
    public int? IdCampaniaSimulacion { get; set; }
}

public sealed class MlModel
{
    public string Version { get; set; } = "";
    public string Metadata { get; set; } = "{}";
    public DateTime ObservedAt { get; set; } = DateTime.UtcNow;
}

public sealed class MlExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DatasetId { get; set; }
    public string Kind { get; set; } = "historical";
    public string State { get; set; } = "queued";
    public string Profile { get; set; } = "nginx-request-v1";
    public string? ModelVersion { get; set; }
    public string? ModelMetadata { get; set; }
    public int WindowMinutes { get; set; } = 5;
    public double Speed { get; set; } = 60;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public long Read { get; set; }
    public long Valid { get; set; }
    public long Excluded { get; set; }
    public long Rejected { get; set; }
    public long Empty { get; set; }
    public long LastLine { get; set; }
    public long Offset { get; set; }
    public string? Error { get; set; }
    public string Reasons { get; set; } = "{}";
    public string Checkpoint { get; set; } = "";
    public Guid? Lease { get; set; }
    public DateTime? LeaseUntil { get; set; }
}

public sealed class HttpAnalysisResult
{
    public long Id { get; set; }
    public Guid ExecutionId { get; set; }
    public long Line { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Subject { get; set; } = "";
    public string Route { get; set; } = "";
    public string Method { get; set; } = "";
    public int Status { get; set; }
    public int Cluster { get; set; }
    public double Strength { get; set; }
    public double AnomalyScore { get; set; }
    public bool Noise { get; set; }
    public bool Candidate { get; set; }
    public double Threshold { get; set; }
    public string Features { get; set; } = "{}";
}

public sealed class HttpAnalysisReview
{
    public long ResultId { get; set; }
    public string State { get; set; } = "pending";
    public string Notes { get; set; } = "";
    public int AnalystId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
