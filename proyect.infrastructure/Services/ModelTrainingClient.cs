using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;

namespace proyect.infrastructure.Services;

public sealed class ModelTrainingClient(HttpClient http, IConfiguration configuration) : IModelTrainingClient
{
    public Task<JsonElement> GetAsync(string resource, CancellationToken ct) =>
        resource is "capabilities" or "jobs" or "models" ? SendAsync(resource, null, ct) : throw new ArgumentException("Recurso inválido.");
    public Task<JsonElement> StartAsync(ModelTrainingPayload payload, CancellationToken ct) => SendAsync("jobs", payload, ct);
    public Task<JsonElement> ValidateAsync(Guid id, CancellationToken ct) => SendAsync($"jobs/{id}/validate", new { }, ct);
    public Task<JsonElement> ActivateAsync(string version, CancellationToken ct) => SendAsync($"models/{Uri.EscapeDataString(version)}/activate", new { }, ct);

    private async Task<JsonElement> SendAsync(string path, object? body, CancellationToken ct)
    {
        if (!Uri.TryCreate(configuration["Ml:BaseUrl"]?.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BusinessRuleException("Configura Ml:BaseUrl para conectar FastAPI.", BusinessError.Unavailable);

        if (string.IsNullOrWhiteSpace(configuration["Ml:ApiKey"]))
            throw new BusinessRuleException("Configura la misma clave en Ml:ApiKey y API_KEY del servicio de modelos para administrar ajustes.", BusinessError.Unavailable);

        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(uri, "training/" + path));
        request.Headers.Add("X-API-Key", configuration["Ml:ApiKey"]);

        if (body is not null)
            request.Content = JsonContent.Create(body);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                throw new BusinessRuleException(code switch {
                    409 => "El ajuste está ocupado o la versión todavía no cumple las condiciones de validación. Actualiza el historial.",
                    422 => "El servicio rechazó los datos o parámetros del ajuste. Revisa la selección y el periodo.",
                    404 => "El servicio no dispone de este ajuste o necesita actualizarse para admitir CU-14.",
                    _ => "No se pudo administrar el modelo. Comprueba el servicio FastAPI y su clave de acceso."
                }, code == 409 ? BusinessError.Conflict : code == 422 ? BusinessError.Validation : BusinessError.Unavailable);
            }
            return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new BusinessRuleException("No se pudo confirmar la respuesta de FastAPI. Actualiza el historial antes de repetir la operación.", BusinessError.Unavailable);
        }
    }
}
