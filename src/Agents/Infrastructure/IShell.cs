using Core.Contracts;

namespace IAW.Agents.System;

public interface IShell : IAgent
{
    Task<CommandResult> ExecuteAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default);
    Task<CommandResult> RunDotnetAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default);
    Task<ShellMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record CommandResult(
    [property: Id(0)] int ExitCode,
    [property: Id(1)] string Output,
    [property: Id(2)] string Error,
    [property: Id(3)] TimeSpan Duration);

[GenerateSerializer]
public record ShellMetrics(
    [property: Id(0)] int TotalCommands,
    [property: Id(1)] int FailedCommands,
    [property: Id(2)] Dictionary<string, int> CommandFrequency,
    [property: Id(3)] TimeSpan AverageExecutionTime);
