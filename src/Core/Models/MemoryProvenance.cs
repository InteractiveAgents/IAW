namespace Core.Models;

[GenerateSerializer]
public record MemoryProvenance(
    [property: Id(0)] string Source,
    [property: Id(1)] string? TaskId,
    [property: Id(2)] string? AgentId,
    [property: Id(3)] string? EventType,
    [property: Id(4)] DateTimeOffset ObservedAt,
    [property: Id(5)] string? ConversationId,
    [property: Id(6)] float TrustScore);
