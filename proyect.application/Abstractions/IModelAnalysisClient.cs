using proyect.application.Dtos;

namespace proyect.application.Abstractions;

public interface IModelAnalysisClient
{
    Task<ModelInfo> GetModelAsync(CancellationToken ct);
    Task<IReadOnlyList<ModelPrediction>> AnalyzeAsync(IReadOnlyList<HttpFeatures> records, CancellationToken ct);
}

public interface IHttpDatasetStorage
{
    Task<StoredHttpFile> SaveAsync(Guid id, string fileName, Stream content, CancellationToken ct);
    Stream Open(Guid id);
    void Delete(Guid id);
}

public interface IModelRegistry
{
    Task<string> RegisterAsync(ModelInfo info, CancellationToken ct);
}
