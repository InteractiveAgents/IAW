namespace IAW.Core;

[GenerateSerializer]
public record AgentEvent(
    [property: Id(0)] string EventName,
    [property: Id(1)] string SourceAgentId,
    [property: Id(2)] string CorrelationId,
    [property: Id(3)] DateTimeOffset Timestamp,
    [property: Id(4)] Dictionary<string, object> Payload);