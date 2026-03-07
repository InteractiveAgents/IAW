namespace IAW.Core.Messages;

[GenerateSerializer]
public record StateChangedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Key,
    [property: Id(4)] string OldValue,
    [property: Id(5)] string NewValue) : IEvent;
