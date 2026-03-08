using Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record SpecReadyEvent(
    [property: Id(0)] string SpecId,
    [property: Id(1)] string InterfaceCode,
    [property: Id(2)] string Description,
    [property: Id(3)] string SourceAgentId,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp) : IEvent;
