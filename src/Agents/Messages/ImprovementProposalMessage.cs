using Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record ImprovementProposalMessage(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string TargetFile,
    [property: Id(2)] string Description,
    [property: Id(3)] string Category,
    [property: Id(4)] string Priority,
    [property: Id(5)] string SourceAgentId,
    [property: Id(6)] string CorrelationId,
    [property: Id(7)] DateTimeOffset Timestamp) : IAgentMessage;
