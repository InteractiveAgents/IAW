namespace Core.V3.Messages;

[GenerateSerializer]
public record ReviewRequestNotification(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string FilePath,
    [property: Id(4)] string Description) : INotification;
