using System.Diagnostics;
using System.Text.RegularExpressions;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Core.Contracts;
using Core.AI;
using Core.AI.Models;
using Core.Communication;
using Core.Communication.Messages;

namespace IAW.Agents.CSharp;

public partial class DotNetAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<global::Core.Contracts.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    IHttpClientFactory httpClientFactory)
    : Agent(state, eventLog, chatClient, history, trackingItems), IDotNet
{
    private const string EditorConfigUrl =
        "https://raw.githubusercontent.com/dotnet/runtime/main/.editorconfig";

    protected override string DisplayName => "DotNet Toolchain";
    protected override string Instructions =>
        "You are a .NET toolchain agent. You run tests, format code, and manage builds.";

    public async Task<TestRunResult> TestAsync(string? filter = null, CancellationToken ct = default)
    {
        var solutionPath = FindSolutionFromWorkspace();
        if (solutionPath is null)
            return new TestRunResult(false, 0, 0, 0, "No solution found in workspace. Set workspace first.");

        return await RunTestsAsync(solutionPath, filter, ct);
    }

    public async Task<string> FormatAsync(CancellationToken ct = default)
    {
        var solutionPath = FindSolutionFromWorkspace();
        if (solutionPath is null)
            return "No solution found in workspace. Set workspace first.";

        var result = await RunFormatAsync(solutionPath, ct);
        return result.Summary;
    }

    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage message, CancellationToken ct = default)
    {
        var solutionPath = !string.IsNullOrEmpty(message.ProjectPath)
            ? message.ProjectPath
            : FindSolutionPath(message.FilePath);

        if (solutionPath is not null)
        {
            await RunTestsAsync(solutionPath, null, ct);
            return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
        }

        return new MessageReceipt(false, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "No solution path found");
    }

    public Task<bool> CanReceiveAsync(CancellationToken ct = default) => Task.FromResult(true);

    public override async Task HandleEvent(AgentEvent agentEvent, CancellationToken ct = default)
    {
        if (agentEvent.EventName != "code.changed") return;

        var solutionPath = agentEvent.Payload.TryGetValue("SolutionPath", out var sp)
            ? sp.ToString()!
            : agentEvent.Payload.TryGetValue("FilePath", out var fp)
                ? FindSolutionPath(fp.ToString()!)
                : FindSolutionFromWorkspace();

        if (solutionPath is not null)
        {
            await RunTestsAsync(solutionPath, null, ct);
            await RunFormatAsync(solutionPath, ct);
        }
    }

    private async Task<TestRunResult> RunTestsAsync(string solutionPath, string? filter, CancellationToken ct)
    {
        State["solution-path"] = new StateEntry("solution-path", solutionPath);

        var args = $"test \"{solutionPath}\" --no-build --verbosity minimal";
        if (!string.IsNullOrEmpty(filter))
            args += $" --filter \"{filter}\"";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return new TestRunResult(false, 0, 0, 0, "Failed to start dotnet test");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var fullOutput = output + error;
        var (total, passed, failed) = ParseTestOutput(fullOutput);
        var allPassed = failed == 0 && total > 0;

        var result = new TestRunResult(allPassed, total, passed, failed, fullOutput);

        State["last-run-total"] = new StateEntry("last-run-total", total);
        State["last-run-passed"] = new StateEntry("last-run-passed", passed);
        State["last-run-failed"] = new StateEntry("last-run-failed", failed);
        await WriteStateAsync(ct);

        var eventName = allPassed ? "tests.passed" : "tests.failed";
        await PublishAsync(eventName, new Dictionary<string, object>
        {
            ["SolutionPath"] = solutionPath,
            ["Total"] = total,
            ["Passed"] = passed,
            ["Failed"] = failed
        }, ct);

        return result;
    }

    private async Task<FormatResult> RunFormatAsync(string solutionPath, CancellationToken ct)
    {
        State["last-format-path"] = new StateEntry("last-format-path", solutionPath);

        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var editorConfigCreated = await EnsureEditorConfigAsync(solutionDir, ct);

        var (success, output) = await RunDotnetFormatAsync(solutionPath, ct);
        var changedFiles = ParseChangedFiles(output);

        State["last-format-result"] = new StateEntry("last-format-result", success ? "pass" : "fail");
        if (editorConfigCreated)
            State["editorconfig-source"] = new StateEntry("editorconfig-source", EditorConfigUrl);
        await WriteStateAsync(ct);

        await PublishAsync("code.formatted", new Dictionary<string, object>
        {
            ["SolutionPath"] = solutionPath,
            ["Success"] = success,
            ["ChangedFiles"] = string.Join(",", changedFiles),
            ["EditorConfigCreated"] = editorConfigCreated
        }, ct);

        var summary = editorConfigCreated
            ? $"Formatted {changedFiles.Count} files. Created .editorconfig from dotnet/runtime."
            : $"Formatted {changedFiles.Count} files.";

        return new FormatResult(success, summary, changedFiles, editorConfigCreated);
    }

    private async Task<bool> EnsureEditorConfigAsync(string directory, CancellationToken ct)
    {
        var editorConfigPath = Path.Combine(directory, ".editorconfig");
        if (File.Exists(editorConfigPath))
            return false;

        using var httpClient = httpClientFactory.CreateClient();
        var content = await httpClient.GetStringAsync(EditorConfigUrl, ct);
        await File.WriteAllTextAsync(editorConfigPath, content, ct);
        return true;
    }

    private static async Task<(bool Success, string Output)> RunDotnetFormatAsync(
        string solutionPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet", $"format \"{solutionPath}\" --verbosity diagnostic")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return (false, "Failed to start dotnet format");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode == 0, output + error);
    }

    private static (int Total, int Passed, int Failed) ParseTestOutput(string output)
    {
        var match = TestResultRegex().Match(output);
        if (match.Success)
        {
            var passed = int.TryParse(match.Groups["passed"].Value, out var p) ? p : 0;
            var failed = int.TryParse(match.Groups["failed"].Value, out var f) ? f : 0;
            var total = int.TryParse(match.Groups["total"].Value, out var t) ? t : passed + failed;
            return (total, passed, failed);
        }
        return (0, 0, 0);
    }

    private static List<string> ParseChangedFiles(string output)
    {
        var files = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Formatted code file", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                files.Add(trimmed);
        }
        return files;
    }

    private string? FindSolutionFromWorkspace()
    {
        var workspace = GetWorkspacePath();
        if (workspace is null) return null;
        return FindSolutionPath(workspace);
    }

    private static string? FindSolutionPath(string startPath)
    {
        var dir = File.Exists(startPath) ? Path.GetDirectoryName(startPath) : startPath;
        while (dir is not null)
        {
            var slnFiles = Directory.GetFiles(dir, "*.sln");
            if (slnFiles.Length > 0) return slnFiles[0];
            var slnxFiles = Directory.GetFiles(dir, "*.slnx");
            if (slnxFiles.Length > 0) return slnxFiles[0];
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [GeneratedRegex(@"Failed:\s+(?<failed>\d+).*?Passed:\s+(?<passed>\d+).*?Total:\s+(?<total>\d+)", RegexOptions.Singleline)]
    private static partial Regex TestResultRegex();
}
