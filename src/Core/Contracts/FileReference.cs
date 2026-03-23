namespace Core.Contracts;

[GenerateSerializer]
public sealed record FileReference(
    [property: Id(0)] string BlobUri,
    [property: Id(1)] string FileName,
    [property: Id(2)] string MimeType,
    [property: Id(3)] long SizeBytes,
    [property: Id(4)] bool Ingested,
    [property: Id(5)] DateTimeOffset UploadedAt);