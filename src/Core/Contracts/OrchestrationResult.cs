namespace Core.Contracts;

[GenerateSerializer]
public record OrchestrationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Summary,
    [property: Id(2)] string WorkspacePath,
    [property: Id(3)] List<string> Artifacts,
    [property: Id(4)] Dictionary<string, string>? Metrics,
    [property: Id(5)] string? ErrorDetail,
    [property: Id(6)] string? TaskId = null);
