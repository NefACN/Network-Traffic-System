using System.Text.Json;
using proyect.application.Dtos;

namespace proyect.application.Abstractions;

public interface IModelTrainingClient
{
    Task<JsonElement> GetAsync(string resource, CancellationToken ct);
    Task<JsonElement> StartAsync(ModelTrainingPayload payload, CancellationToken ct);
    Task<JsonElement> ValidateAsync(Guid id, CancellationToken ct);
    Task<JsonElement> ActivateAsync(string version, CancellationToken ct);
}
