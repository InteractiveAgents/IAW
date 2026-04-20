namespace Core.Contracts.Security;

[GenerateSerializer]
public sealed record ToolAuthorizationRequest(
    [property: Id(0)] string AgentId,
    [property: Id(1)] string AgentDisplayName,
    [property: Id(2)] string ToolName,
    [property: Id(3)] string ArgumentsJson,
    [property: Id(4)] IReadOnlyList<string> RecentMessages);
