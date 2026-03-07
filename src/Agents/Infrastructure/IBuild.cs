using IAW.Core;

namespace IAW.Agents.Infrastructure;

public interface IBuild : IAgent
{
    Task<BuildResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default);
    Task<TestResult> TestAsync(string projectPath, string? filter = null, CancellationToken ct = default);
    Task<BuildMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record BuildResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Output,
    [property: Id(2)] int Warnings,
    [property: Id(3)] int Errors,
    [property: Id(4)] TimeSpan Duration,
    [property: Id(5)] string[] Diagnostics);

[GenerateSerializer]
public record TestResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Output,
    [property: Id(2)] int Total,
    [property: Id(3)] int Passed,
    [property: Id(4)] int Failed,
    [property: Id(5)] TimeSpan Duration);

[GenerateSerializer]
public record BuildMetrics(
    [property: Id(0)] int TotalBuilds,
    [property: Id(1)] int FailedBuilds,
    [property: Id(2)] TimeSpan AverageBuildTime,
    [property: Id(3)] int TotalWarnings,
    [property: Id(4)] int TotalErrors);
