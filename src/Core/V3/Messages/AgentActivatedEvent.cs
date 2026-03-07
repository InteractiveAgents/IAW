namespace Core.V3.Messages;

[GenerateSerializer]
public record AgentActivatedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string AgentType) : IEvent;
