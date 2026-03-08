using Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record CodeChangedEvent(
    [property: Id(0)] string[] ChangedFiles,
    [property: Id(1)] string Author,
    [property: Id(2)] string Description,
    [property: Id(3)] string SourceAgentId,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp) : IEvent;
