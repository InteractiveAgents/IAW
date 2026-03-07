namespace IAW.Core;

public enum AgentResponseKind { Text, ToolCall, ToolResult, Error, Final }

[GenerateSerializer]
public record AgentResponse(
    [property: Id(0)] AgentResponseKind Kind,
    [property: Id(1)] string Content,
    [property: Id(2)] string? ToolName = null,
    [property: Id(3)] Dictionary<string, object>? Metadata = null);
