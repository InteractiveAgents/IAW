namespace IAW.Core.Messages;

[GenerateSerializer]
public record AssignTaskCommand(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Description,
    [property: Id(4)] string? WorkspacePath = null) : ICommand;
