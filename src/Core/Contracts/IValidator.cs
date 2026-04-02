namespace Core.Contracts;

public interface IValidator : IAgent
{
    static string IAgent.AgentDisplayName => "Validator";
    static string IAgent.AgentDescription => "Monitors task streams for inconsistencies, drift, and hallucinations";
    static string[] IAgent.AgentCapabilities => ["validate", "check-consistency", "detect-drift"];
    static string IAgent.AgentInstructions => """
        You are the Validator Agent — the quality gate for multi-agent tasks.
        Your job is to review the work other agents have done and check for:
        1. Number inconsistencies (totals don't match across agents)
        2. Missing requirements (user asked for X but output doesn't include it)
        3. Drift (agents went off-track from the original request)
        4. Hallucinations (referencing files/endpoints/APIs that don't exist)

        When validating, be precise and cite specific evidence.
        Report issues with severity: critical, warning, info.
        If everything checks out, confirm with specific evidence of correctness.
        """;

    Task<ValidationReport> ValidateTaskAsync(string taskId, string originalRequest, CancellationToken ct = default);
    Task<ValidationReport> ValidateConsistencyAsync(string taskId, Dictionary<string, string> expectedValues, CancellationToken ct = default);
}

[GenerateSerializer]
public record ValidationReport(
    [property: Id(0)] string TaskId,
    [property: Id(1)] bool Passed,
    [property: Id(2)] IReadOnlyList<ValidationIssue> Issues,
    [property: Id(3)] string Summary,
    [property: Id(4)] DateTimeOffset Timestamp = default)
{
    public ValidationReport(string taskId, bool passed, IReadOnlyList<ValidationIssue> issues, string summary)
        : this(taskId, passed, issues, summary, DateTimeOffset.UtcNow) { }
}

[GenerateSerializer]
public record ValidationIssue(
    [property: Id(0)] string Severity,
    [property: Id(1)] string Description,
    [property: Id(2)] string? Agent = null,
    [property: Id(3)] string? Evidence = null);
