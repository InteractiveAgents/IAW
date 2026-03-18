using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Core.Services;

public sealed class BlobFileStorage
{
    private const string ContainerName = "files";
    private readonly Lazy<Task<BlobContainerClient>> _container;

    public BlobFileStorage(BlobServiceClient blobServiceClient)
    {
        _container = new Lazy<Task<BlobContainerClient>>(async () =>
        {
            var container = blobServiceClient.GetBlobContainerClient(ContainerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.None);
            return container;
        });
    }

    public async Task<string> UploadAsync(Stream stream, string path, string contentType)
    {
        var container = await _container.Value;
        var blobClient = container.GetBlobClient(path);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        };

        await blobClient.UploadAsync(stream, uploadOptions);
        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadAsync(string blobUri)
    {
        var container = await _container.Value;
        var blobName = new BlobUriBuilder(new Uri(blobUri)).BlobName;
        var blobClient = container.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }
}
