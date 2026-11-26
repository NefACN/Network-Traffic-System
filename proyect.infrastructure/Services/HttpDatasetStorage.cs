using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using proyect.application.Abstractions;
using proyect.application.Dtos;
using proyect.application.Exceptions;

namespace proyect.infrastructure.Services;

public sealed class HttpDatasetStorage(IConfiguration configuration) : IHttpDatasetStorage
{
    private string Root => Path.GetFullPath(configuration["Ml:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "http"));
    private string FilePath(Guid id) => Path.Combine(Root, id.ToString("N") + ".log");
    public async Task<StoredHttpFile> SaveAsync(Guid id, string fileName, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var maximum = configuration.GetValue<long?>("Ml:MaxFileBytes") ?? 2_000_000_000;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        var buffer = new byte[131072];
        try
        {
            await using (var output = new FileStream(FilePath(id), FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                int count;
                while ((count = await content.ReadAsync(buffer, ct)) > 0)
                {
                    size += count;
                    if (size > maximum)
                        throw new BusinessRuleException("El archivo supera el tamaño admitido.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            if (size == 0)
                throw new BusinessRuleException("El archivo está vacío.");
            var name = Path.GetFileName(fileName);
            return new StoredHttpFile(name[..Math.Min(name.Length, 200)], size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch { Delete(id); throw; }
    }
    public Stream Open(Guid id) => new FileStream(FilePath(id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 131072, true);
    public void Delete(Guid id) { if (File.Exists(FilePath(id))) File.Delete(FilePath(id)); }
}
