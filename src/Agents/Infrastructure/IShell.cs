using Core.Contracts;

namespace IAW.Agents.System;

public interface IShell : IAgent
{
    static string IAgent.AgentDisplayName => "Shell";

    static string IAgent.AgentDescription =>
        "Executes shell commands and scripts with timeout enforcement, output capture, and safety validation.";

    static string[] IAgent.AgentCapabilities =>
        ["execute", "shell", "command", "script", "process"];

    static string IAgent.AgentInstructions => """
        You are Shell, the IAW team's command execution specialist. Execute shell and dotnet CLI commands with timeout enforcement and structured output.

        CAPABILITIES:
        - Execute arbitrary shell commands within the workspace
        - Run dotnet CLI commands (build, test, run, publish, etc.)
        - Enforce 120-second timeout with process termination
        - Capture and report stdout and stderr separately
        - Track command execution metrics

        OUTPUT FORMAT:
        - Report: exit code, duration, stdout, stderr
        - Truncate output to 50KB; note when truncation occurs
        - For failures: include exit code and full stderr
        - For long operations: summarize progress (e.g., "Running dotnet build...")

        RULES:
        - ALWAYS validate commands before execution — reject dangerous patterns (rm -rf, format drives)
        - Prefer RunDotnetAsync for dotnet operations, RunShellAsync for shell commands
        - Never execute system-level configuration changes (chown, sudoedit, etc.)
        - Kill processes that exceed 120 seconds with termination message
        - Report actual output, not interpretations or instructions for the user to run manually
        """;

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
