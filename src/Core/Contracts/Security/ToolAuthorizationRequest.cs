namespace Core.Contracts.Security;

[GenerateSerializer]
public sealed record ToolAuthorizationRequest(
    [property: Id(0)] string AgentId,
    [property: Id(1)] string AgentDisplayName,
    [property: Id(2)] string ToolName,
    [property: Id(3)] string ArgumentsPreview,
    [property: Id(4)] string ThreadId,
    [property: Id(5)] string UserId,
    [property: Id(6)] IReadOnlyList<string> RecentTurnSnippets);
