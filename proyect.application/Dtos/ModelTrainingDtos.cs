using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace proyect.application.Dtos;

public sealed record StartModelTrainingRequest(
    Guid ExecutionId,
    [Range(5, 2000)] int MinClusterSize = 200,
    [Range(1, 1000)] int MinSamples = 20,
    [Range(100, 30000)] int MaxRecords = 20000,
    [Range(0, int.MaxValue)] int Seed = 42,
    [Range(0d, 1d)] double Threshold = .8,
    // periodo opcional para ajustar dentro de una ventana temporal
    DateTime? Desde = null, DateTime? Hasta = null);

public sealed record ModelTrainingOptions(
    [property: JsonPropertyName("min_cluster_size")] int MinClusterSize,
    [property: JsonPropertyName("min_samples")] int MinSamples, int Seed,
    [property: JsonPropertyName("umbral_alerta")] double Threshold);
public sealed record ModelTrainingRecord(
    [property: JsonPropertyName("occurred_at")] DateTime OccurredAt, HttpFeatures Features);
public sealed record ModelTrainingPayload(
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_profile")] string SourceProfile,
    [property: JsonPropertyName("source_count")] long SourceCount,
    ModelTrainingOptions Opciones, IReadOnlyList<ModelTrainingRecord> Registros);
