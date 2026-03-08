namespace Core.Messages;

[GenerateSerializer]
public record CodeChangedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string[] FilePaths,
    [property: Id(4)] string? CommitSha = null) : IEvent;
