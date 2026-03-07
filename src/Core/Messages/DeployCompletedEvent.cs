namespace IAW.Core.Messages;

[GenerateSerializer]
public record DeployCompletedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] bool Success,
    [property: Id(4)] string Environment,
    [property: Id(5)] string? Version = null) : IEvent;
