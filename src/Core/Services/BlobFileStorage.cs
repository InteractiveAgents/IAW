using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Core.Services;

public sealed class BlobFileStorage(BlobServiceClient blobServiceClient)
{
    private const string ContainerName = "files";
    private BlobContainerClient? _containerClient;

    private async Task<BlobContainerClient> GetContainerAsync()
    {
        if (_containerClient is not null) return _containerClient;

        var container = blobServiceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);
        _containerClient = container;
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
        var uri = new Uri(blobUri);
        // Extract blob path from the URI (everything after the container name)
        var container = await GetContainerAsync();
        var blobName = uri.AbsolutePath
            .TrimStart('/')
            .Substring(ContainerName.Length + 1) // skip "files/"
            .TrimStart('/');

        // Handle the account name prefix in the path (e.g., /devstoreaccount1/files/...)
        if (blobName.Contains(ContainerName + "/"))
            blobName = blobName[(blobName.IndexOf(ContainerName + "/") + ContainerName.Length + 1)..];

        var blobClient = container.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }
}
