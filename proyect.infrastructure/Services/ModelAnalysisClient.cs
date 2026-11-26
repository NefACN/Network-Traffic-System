using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;

namespace proyect.infrastructure.Services;

public sealed class ModelAnalysisClient(HttpClient http, IConfiguration configuration) : IModelAnalysisClient
{
    private async Task<T> SendAsync<T>(string path, object? body, CancellationToken ct)
    {
        var baseUrl = configuration["Ml:BaseUrl"];
        if (!Uri.TryCreate(baseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BusinessRuleException("Configura Ml:BaseUrl para conectar FastAPI.", BusinessError.Unavailable);
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, new Uri(uri, path));
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(configuration["Ml:ApiKey"]))
            request.Headers.Add("X-API-Key", configuration["Ml:ApiKey"]);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new BusinessRuleException($"FastAPI devolvió HTTP {(int)response.StatusCode}. Comprueba conexión, clave y modelo.", BusinessError.Unavailable);
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        { throw new BusinessRuleException("No se pudo obtener una respuesta válida de FastAPI.", BusinessError.Unavailable); }
    }

    public async Task<ModelInfo> GetModelAsync(CancellationToken ct)
    {
        var info = await SendAsync<ModelInfo>("model-info", null, ct);
        string[] columns = ["codigo_status", "bytes_enviados", "tiempo_respuesta_ms", "upstream_response_ms", "interval_request_ms", "request_sessions", "errors_ip", "entropy_ruta", "hora", "dia_semana", "metodo"];
        if (string.IsNullOrWhiteSpace(info.Version) || info.Version.Length > 120 || info.Columns is null || !info.Columns.SequenceEqual(columns) || !double.IsFinite(info.Threshold) || info.Threshold is < 0 or > 1)
            throw new BusinessRuleException("El contrato del modelo no es compatible con nginx-request-v1.", BusinessError.Unavailable);
        return info;
    }

    public async Task<IReadOnlyList<ModelPrediction>> AnalyzeAsync(IReadOnlyList<HttpFeatures> records, CancellationToken ct)
    {
        var result = await SendAsync<PredictionBatch>("predict/batch", new { registros = records }, ct);
        if (result.Total != records.Count || result.Resultados is null || result.Resultados.Count != records.Count)
            throw new BusinessRuleException("FastAPI devolvió una cantidad de resultados incorrecta.", BusinessError.Unavailable);
        return result.Resultados;
    }
    private sealed record PredictionBatch(int Total, List<ModelPrediction> Resultados);
}
