namespace Core.Messages.Events;

[GenerateSerializer]
public record TaskCompletedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string TaskId,
    [property: Id(4)] bool Success,
    [property: Id(5)] string? Summary = null) : ITaskStreamEvent;