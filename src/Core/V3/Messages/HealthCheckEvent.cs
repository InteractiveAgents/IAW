namespace Core.V3.Messages;

[GenerateSerializer]
public record HealthCheckEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string ServiceName,
    [property: Id(4)] bool Healthy,
    [property: Id(5)] double? ResponseTimeMs = null) : IEvent;
