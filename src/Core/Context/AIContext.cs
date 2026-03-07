namespace IAW.Core.Context;

[GenerateSerializer]
public sealed record AIContext(
    [property: Id(0)] IReadOnlyList<ChatMessage> AdditionalMessages,
    [property: Id(1)] IDictionary<string, string>? Metadata = null)
{
    public static AIContext Empty => new(Array.Empty<ChatMessage>());
}
