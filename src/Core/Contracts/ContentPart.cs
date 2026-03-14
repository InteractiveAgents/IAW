using System.Text.Json.Serialization;

namespace Core.Contracts;

[GenerateSerializer]
[JsonDerivedType(typeof(TextContent))]
[JsonDerivedType(typeof(ImageContent))]
[JsonDerivedType(typeof(FileContent))]
public abstract record ContentPart;

[GenerateSerializer]
public sealed record TextContent([property: Id(0)] string Text) : ContentPart;

[GenerateSerializer]
public sealed record ImageContent(
    [property: Id(0)] string BlobUri,
    [property: Id(1)] string MimeType,
    [property: Id(2)] string? Caption) : ContentPart;

[GenerateSerializer]
public sealed record FileContent(
    [property: Id(0)] string BlobUri,
    [property: Id(1)] string FileName,
    [property: Id(2)] string MimeType,
    [property: Id(3)] long SizeBytes,
    [property: Id(4)] bool Ingested) : ContentPart;
