namespace Core.V3.Messages;

[GenerateSerializer]
public record ProgressNotification(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Step,
    [property: Id(4)] string Status,
    [property: Id(5)] float? Progress = null) : INotification;
