using Core.Communication;
using Core.Communication.Messages;
using Core.Contracts;

namespace IAW.Agents.Coding;

public interface IDotNet : IAgent, IReceiver<CodeChangedMessage>
{
    Task<BuildRunResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default);
    Task<TestRunResult> TestAsync(string? filter = null, CancellationToken ct = default);
    Task<string> FormatAsync(CancellationToken ct = default);
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
