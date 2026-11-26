using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace proyect.application.Dtos;

public sealed record ModelInfo(
    string Version,
    [property: JsonPropertyName("columnas_entrada")] string[] Columns,
    [property: JsonPropertyName("min_cluster_size")] int MinClusterSize,
    [property: JsonPropertyName("min_samples")] int MinSamples,
    [property: JsonPropertyName("umbral_alerta")] double Threshold,
    [property: JsonPropertyName("estado_alertas")] string AlertState,
    [property: JsonPropertyName("n_train")] int TrainingCount,
    [property: JsonPropertyName("escalado")] string Scaling);

public sealed record HttpFeatures(
    [property: JsonPropertyName("codigo_status")] int Status,
    [property: JsonPropertyName("bytes_enviados")] long Bytes,
    [property: JsonPropertyName("tiempo_respuesta_ms")] double? ResponseMs,
    [property: JsonPropertyName("upstream_response_ms")] double? UpstreamMs,
    [property: JsonPropertyName("interval_request_ms")] double? IntervalMs,
    [property: JsonPropertyName("request_sessions")] int SessionRequests,
    [property: JsonPropertyName("errors_ip")] double ErrorRate,
    [property: JsonPropertyName("entropy_ruta")] double Entropy,
    [property: JsonPropertyName("hora")] int Hour,
    [property: JsonPropertyName("dia_semana")] int Day,
    [property: JsonPropertyName("metodo")] string Method);

public sealed record ModelPrediction(int Cluster,
    [property: JsonPropertyName("fuerza")] double Strength,
    [property: JsonPropertyName("anomaly_score")] double Score,
    [property: JsonPropertyName("es_ruido")] bool Noise,
    [property: JsonPropertyName("alerta_candidata")] bool Candidate,
    [property: JsonPropertyName("umbral_alerta")] double Threshold,
    [property: JsonPropertyName("modelo_version")] string Version);

public sealed record PreparedHttpEvent(
    long Line,
    DateTime OccurredAt,
    string Subject,
    string Route,
    HttpFeatures Features
    );

public sealed record PreparedHttpBatch(
    Guid Lease,
    long LastLine,
    long Offset,
    long Read,
    long Valid,
    long Excluded,
    long Rejected,
    long Empty,
    DateTime? From,
    DateTime? To,
    Dictionary<string, long> Reasons,
    string Checkpoint,
    bool Completed,
    List<PreparedHttpEvent> Records
    );

public sealed record StartAnalysisRequest(
    Guid DatasetId,
    [Range(5, 15)] int WindowMinutes = 5,
    [Range(0.1, 10000)] double Speed = 60
    );

public sealed record ConfirmDatasetRequest(
    Guid ValidationId,
    [Required, StringLength(120)] string Name,
    [Required, StringLength(300)] string Origin,
    int? IdSucursal = null);

// al confirmar un incidente se precisa sucursal, area y tema para la sugerencia de prevencion
public sealed record ReviewHttpRequest(
    [Required] string State,
    [StringLength(2000)] string Notes,
    bool ConfirmIncident = false,
    int? IdSucursal = null,
    int? IdArea = null,
    int? IdCategoria = null
    );

public sealed record LiveSourceRequest(
    [Required, StringLength(120)] string Name,
    [StringLength(200)] string FileName,
    int? IdSucursal = null
    );

public sealed record AgentFailureRequest(Guid Lease, string Code);

public sealed record StoredHttpFile(string FileName, long Size, string Sha256);
