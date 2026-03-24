using Core.AI;
using Core.AI.Models;
using Core.Communication;
using Core.Communication.Messages;
using Core.Contracts;
using IAW.Agents.System;
using IAW.Core;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IAW.Agents.Coding;

public partial class DotNetAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient,
    IHttpClientFactory httpClientFactory)
    : Agent<IDotNet>(durableState, chatClient), IDotNet
{
    private const string EditorConfigUrl =
        "https://raw.githubusercontent.com/dotnet/runtime/main/.editorconfig";

    public async Task<BuildRunResult> BuildAsync(
        string projectPath, string configuration = "Debug", CancellationToken ct = default)
    {
        var resolvedPath = ResolveProjectPath(projectPath);
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("dotnet", $"build \"{resolvedPath}\" -c {configuration}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new BuildRunResult(false, "Failed to start build process", 0, 1, sw.Elapsed, ["Process start failed"]);
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        sw.Stop();

        var fullOutput = output + error;
        var warnings = CountBuildPattern(fullOutput, BuildWarningRegex());
        var errors = CountBuildPattern(fullOutput, BuildErrorRegex());
        var diagnostics = ExtractBuildDiagnostics(fullOutput);
        var succeeded = process.ExitCode == 0;

        await PublishAsync(succeeded ? "build.succeeded" : "build.failed", new Dictionary<string, string>
        {
            ["ProjectPath"] = projectPath,
            ["Configuration"] = configuration,
            ["Warnings"] = warnings.ToString(),
            ["Errors"] = errors.ToString(),
            ["DurationMs"] = ((long)sw.Elapsed.TotalMilliseconds).ToString()
        }, ct);

        return new BuildRunResult(succeeded, fullOutput, warnings, errors, sw.Elapsed, diagnostics);
    }

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

    public async Task<CommandResult> RunAsync(
        string projectPath, string? arguments = null, CancellationToken ct = default)
    {
        var resolvedPath = ResolveProjectPath(projectPath);
        var sw = Stopwatch.StartNew();

        var args = $"run --project \"{resolvedPath}\"";
        if (!string.IsNullOrEmpty(arguments))
            args += $" -- {arguments}";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new CommandResult(-1, "", "Failed to start dotnet run", sw.Elapsed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(120_000);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await Task.WhenAll(outputTask, errorTask);
            await process.WaitForExitAsync(timeoutCts.Token);
            sw.Stop();
            return new CommandResult(process.ExitCode, outputTask.Result, errorTask.Result, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            sw.Stop();
            return new CommandResult(-1, "", "dotnet run timed out after 120s", sw.Elapsed);
        }
    }

    public Task<string[]> ListProjectsAsync(string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory))
            return Task.FromResult(Array.Empty<string>());

        var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(directory, "*.sln", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(directory, "*.slnx", SearchOption.AllDirectories))
            .OrderBy(p => p)
            .ToArray();

        return Task.FromResult(projects);
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
        await PublishAsync(eventName, new Dictionary<string, string>
        {
            ["SolutionPath"] = solutionPath,
            ["Total"] = total.ToString(),
            ["Passed"] = passed.ToString(),
            ["Failed"] = failed.ToString()
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

        await PublishAsync("code.formatted", new Dictionary<string, string>
        {
            ["SolutionPath"] = solutionPath,
            ["Success"] = success.ToString(),
            ["ChangedFiles"] = string.Join(",", changedFiles),
            ["EditorConfigCreated"] = editorConfigCreated.ToString()
        }, ct);

        if (changedFiles.Count > 0)
            await PublishToStream(new CodeChangedMessage(solutionPath, "", "dotnet format completed")
            {
                FilePaths = changedFiles,
                SourceAgentId = this.GetPrimaryKeyString()
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

    private string ResolveProjectPath(string projectPath)
    {
        if (File.Exists(projectPath)) return projectPath;
        if (!Directory.Exists(projectPath)) return projectPath;
        return FindSolutionPath(projectPath) ?? projectPath;
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
            var csprojFiles = Directory.GetFiles(dir, "*.csproj");
            if (csprojFiles.Length > 0) return csprojFiles[0];
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static int CountBuildPattern(string input, Regex regex) => regex.Matches(input).Count;

    private static string[] ExtractBuildDiagnostics(string output)
    {
        var diagnostics = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Contains(": error ") || trimmed.Contains(": warning "))
                diagnostics.Add(trimmed);
        }
        return [.. diagnostics];
    }

    [GeneratedRegex(@": warning ")]
    private static partial Regex BuildWarningRegex();

    [GeneratedRegex(@": error ")]
    private static partial Regex BuildErrorRegex();

    [GeneratedRegex(@"Failed:\s+(?<failed>\d+).*?Passed:\s+(?<passed>\d+).*?Total:\s+(?<total>\d+)", RegexOptions.Singleline)]
    private static partial Regex TestResultRegex();
}