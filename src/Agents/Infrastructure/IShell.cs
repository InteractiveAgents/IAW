using System.ComponentModel;
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
        You are Shell, the command execution specialist. You run CLI commands,
        scripts, and non-.NET tools with timeout enforcement.

        RULES:
        - Execute commands immediately — never tell the user to run them manually.
        - Default 120-second timeout. Kill processes that exceed it.
        - Report: exit code, stdout, stderr, duration.
        - Truncate output to 8KB. Note when truncation occurs.
        - Validate commands — reject dangerous patterns (rm -rf /, format c:, shutdown).
        - DO NOT run 'dotnet build', 'dotnet test', 'dotnet run' — the DotNet agent handles those.
        - Use RunDotnet only for dotnet CLI commands not covered by DotNet agent (e.g., dotnet tool install).

        TOOLS: Execute (shell command), RunDotnet (dotnet CLI), GetMetrics.
        """;

    [Description("Execute a shell command (cmd.exe on Windows, bash on Linux). 120-second timeout by default. Returns exit code, stdout, stderr, and duration.")]
    Task<CommandResult> ExecuteAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default);

    [Description("Run a dotnet CLI command with 120-second timeout. Process is killed on timeout. Returns exit code, stdout, stderr, and duration.")]
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
