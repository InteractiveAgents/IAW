using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Core.Services;

namespace Core.Orchestration;

public sealed class CheckpointStore(BlobFileStorage blobStorage, BlobServiceClient blobServiceClient)
{
    const string ContainerName = "files";

    public async Task SaveAsync(string taskId, int stepIndex, object result, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(result);
        var path = BuildPath(taskId, stepIndex);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blobStorage.UploadAsync(stream, path, "application/json");
    }

    public async Task<string?> LoadAsync(string taskId, int stepIndex, CancellationToken ct = default)
    {
        try
        {
            var container = blobServiceClient.GetBlobContainerClient(ContainerName);
            var blobClient = container.GetBlobClient(BuildPath(taskId, stepIndex));
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
            await using var stream = response.Value.Content;
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SaveArtifactAsync(string taskId, Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        var path = $"orchestration/{taskId}/{fileName}";
        await blobStorage.UploadAsync(content, path, mimeType);
    }

    static string BuildPath(string taskId, int stepIndex) =>
        $"orchestration/{taskId}/step-{stepIndex}.json";
}
