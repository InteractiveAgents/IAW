namespace Core.Contracts;

[GenerateSerializer]
public record AgentConfiguration(
    [property: Id(0)] string? DisplayName,
    [property: Id(1)] string? SystemPrompt,
    [property: Id(2)] string[]? ToolNames,
    [property: Id(3)] string? WorkspacePath,
    [property: Id(4)] string[]? SubscribeToStreams);
