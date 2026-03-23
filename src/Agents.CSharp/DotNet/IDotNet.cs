using Core.Communication;
using Core.Communication.Messages;
using Core.Contracts;
using IAW.Agents.System;
using System.ComponentModel;

namespace IAW.Agents.Coding;

public interface IDotNet : IAgent, IReceiver<CodeChangedMessage>
{
    static string IAgent.AgentDisplayName => "DotNet";

    static string IAgent.AgentDescription =>
        "Builds, tests, and formats .NET projects; parses diagnostics and runs test suites with optional filtering.";

    static string[] IAgent.AgentCapabilities =>
        ["build", "test", "format", "diagnose", "dotnet", "csharp"];

    static string IAgent.AgentInstructions => """
        You are DotNet, the .NET toolchain specialist. You build, run, test, publish,
        and scaffold .NET projects. Execute operations immediately and report results.

        RULES:
        - ALWAYS call the appropriate tool — never respond with manual instructions.
        - When given a directory path, Build auto-discovers .csproj/.sln files.
        - For build errors, return the full diagnostic output.
        - For test failures, return pass/fail counts and failing test names.
        - DO NOT execute raw shell commands — use your typed Build/Test/Format tools.
        - DO NOT ask the user for project paths — discover them from the directory.

        TOOLS: Build, Test, Format (auto-registered from interface).
        """;

    [Description("Build a .NET project or solution. Accepts a directory path, .csproj, or .sln — auto-discovers project files from directories. Returns success/failure with error count, warning count, duration, and diagnostics.")]
    Task<BuildRunResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default);

    [Description("Run .NET tests for the workspace project. Optionally filter by test name pattern. Returns pass/fail counts and full output.")]
    Task<TestRunResult> TestAsync(string? filter = null, CancellationToken ct = default);

    [Description("Format C# code in the workspace using dotnet format with editorconfig. Returns summary of changed files.")]
    Task<string> FormatAsync(CancellationToken ct = default);

    [Description("Run a .NET project with dotnet run. Accepts directory or .csproj path — auto-discovers project. 120-second timeout kills the process. Returns exit code, stdout, stderr.")]
    Task<CommandResult> RunAsync(string projectPath, string? arguments = null, CancellationToken ct = default);

    [Description("List all .csproj, .sln, and .slnx files in a directory tree. Use to discover projects before building. Returns array of absolute file paths.")]
    Task<string[]> ListProjectsAsync(string directory, CancellationToken ct = default);
}

[GenerateSerializer]
public sealed record BuildRunResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Output,
    [property: Id(2)] int Warnings,
    [property: Id(3)] int Errors,
    [property: Id(4)] TimeSpan Duration,
    [property: Id(5)] string[] Diagnostics);

[GenerateSerializer]
public sealed record TestRunResult(
    [property: Id(0)] bool AllPassed,
    [property: Id(1)] int Total,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Failed,
    [property: Id(4)] string Output);

[GenerateSerializer]
public sealed record FormatResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Summary,
    [property: Id(2)] IReadOnlyList<string> ChangedFiles,
    [property: Id(3)] bool EditorConfigCreated);