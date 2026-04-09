using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Core.Services;

public sealed class BlobFileStorage
{
    private const string ContainerName = "files";
    private readonly BlobServiceClient _blobServiceClient;
    private BlobContainerClient? _container;

    public BlobFileStorage(BlobServiceClient blobServiceClient) => _blobServiceClient = blobServiceClient;

    private async Task<BlobContainerClient> GetContainerAsync()
    {
        if (_container is not null) return _container;
        var container = _blobServiceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);
        _container = container;
        return container;
    }

    public async Task<string> UploadAsync(Stream stream, string path, string contentType)
    {
        var container = await GetContainerAsync();
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
        var container = await GetContainerAsync();
        var blobName = new BlobUriBuilder(new Uri(blobUri)).BlobName;
        var blobClient = container.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }
}