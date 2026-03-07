namespace Core.V3.Messages;

[GenerateSerializer]
public record BuildCompletedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] bool Success,
    [property: Id(4)] string? CommitSha = null,
    [property: Id(5)] string? Output = null) : IEvent;
