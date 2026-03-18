using System.Diagnostics;
using System.Text.RegularExpressions;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Tools;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Infrastructure;

public partial class BuildAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IBuild
{
    protected override string DisplayName => "Build";
    protected override string Instructions => """
        You are Build, the IAW team's compilation and test specialist. Build projects, run tests, and report diagnostics.

        CAPABILITIES:
        - Build .NET projects and solutions in Debug or Release configuration
        - Run unit tests with optional filtering by fully qualified name
        - Parse and extract build errors and warnings
        - Report test results: total, passed, failed, skipped
        - Track build metrics for trend analysis

        OUTPUT FORMAT:
        Build reports: exit code, error count, warning count, duration (ms)
        Test reports: total count, passed count, failed count, skipped count, duration
        Failures: list first 5 errors/warnings with file and line number
        Failed tests: list test names that failed

        RULES:
        - Always report: exit code, error count, warning count for builds
        - Always report: total, passed, failed for tests
        - When builds fail, include the first 5 error messages with file locations
        - When tests fail, list names of failing tests
        - Never ignore test output — report all results, not summaries
        """;

    protected override IReadOnlyList<AITool> DefineTools()
    {
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new ShellTools(() => GetWorkspacePath() ?? Directory.GetCurrentDirectory()));
        return tools;
    }

    public async Task<BuildResult> BuildAsync(
        string projectPath, string configuration = "Debug", CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("dotnet", $"build \"{projectPath}\" -c {configuration}")
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
            return new BuildResult(false, "Failed to start build process", 0, 1, sw.Elapsed, ["Process start failed"]);
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        sw.Stop();

        var fullOutput = output + error;
        var warnings = CountPattern(fullOutput, WarningRegex());
        var errors = CountPattern(fullOutput, ErrorRegex());
        var diagnostics = ExtractDiagnostics(fullOutput);
        var buildSucceeded = process.ExitCode == 0;

        await RecordBuildResult(buildSucceeded, warnings, errors, sw.Elapsed, ct);

        var eventName = buildSucceeded ? "build.succeeded" : "build.failed";
        await PublishAsync(eventName, new Dictionary<string, object>
        {
            ["ProjectPath"] = projectPath,
            ["Configuration"] = configuration,
            ["Warnings"] = warnings,
            ["Errors"] = errors,
            ["DurationMs"] = (long)sw.Elapsed.TotalMilliseconds
        }, ct);

        return new BuildResult(buildSucceeded, fullOutput, warnings, errors, sw.Elapsed, diagnostics);
    }

    public async Task<TestResult> TestAsync(
        string projectPath, string? filter = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var args = $"test \"{projectPath}\" --verbosity minimal";
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
        {
            sw.Stop();
            return new TestResult(false, "Failed to start test process", 0, 0, 0, sw.Elapsed);
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        sw.Stop();

        var fullOutput = output + error;
        var (total, passed, failed) = ParseTestOutput(fullOutput);
        var testsPassed = failed == 0 && total > 0;

        var eventName = testsPassed ? "tests.passed" : "tests.failed";
        await PublishAsync(eventName, new Dictionary<string, object>
        {
            ["ProjectPath"] = projectPath,
            ["Total"] = total,
            ["Passed"] = passed,
            ["Failed"] = failed,
            ["DurationMs"] = (long)sw.Elapsed.TotalMilliseconds
        }, ct);

        return new TestResult(testsPassed, fullOutput, total, passed, failed, sw.Elapsed);
    }

    public Task<BuildMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalBuilds = GetCounterValue("total-builds");
        var failedBuilds = GetCounterValue("failed-builds");
        var totalBuildTimeMs = GetLongValue("total-build-time-ms");
        var avgBuildTime = totalBuilds > 0
            ? TimeSpan.FromMilliseconds(totalBuildTimeMs / totalBuilds)
            : TimeSpan.Zero;
        var totalWarnings = GetCounterValue("total-warnings");
        var totalErrors = GetCounterValue("total-errors");

        return Task.FromResult(new BuildMetrics(totalBuilds, failedBuilds, avgBuildTime, totalWarnings, totalErrors));
    }

    private async Task RecordBuildResult(bool succeeded, int warnings, int errors, TimeSpan duration, CancellationToken ct)
    {
        IncrementCounter("total-builds");
        if (!succeeded)
            IncrementCounter("failed-builds");

        AddToCounter("total-warnings", warnings);
        AddToCounter("total-errors", errors);

        var totalBuildTimeMs = GetLongValue("total-build-time-ms") + (long)duration.TotalMilliseconds;
        State["total-build-time-ms"] = new StateEntry("total-build-time-ms", totalBuildTimeMs);

        await WriteStateAsync(ct);
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

    private static int CountPattern(string input, Regex regex)
        => regex.Matches(input).Count;

    private static string[] ExtractDiagnostics(string output)
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

    private void IncrementCounter(string key)
    {
        var current = GetCounterValue(key);
        State[key] = new StateEntry(key, current + 1);
    }

    private void AddToCounter(string key, int amount)
    {
        var current = GetCounterValue(key);
        State[key] = new StateEntry(key, current + amount);
    }

    private int GetCounterValue(string key)
    {
        if (!State.TryGetValue(key, out var desc)) return 0;
        return desc.Value is int i ? i : int.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private long GetLongValue(string key)
    {
        if (!State.TryGetValue(key, out var desc)) return 0;
        return desc.Value is long l ? l : long.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    [GeneratedRegex(@": warning ")]
    private static partial Regex WarningRegex();

    [GeneratedRegex(@": error ")]
    private static partial Regex ErrorRegex();

    [GeneratedRegex(@"Failed:\s+(?<failed>\d+).*?Passed:\s+(?<passed>\d+).*?Total:\s+(?<total>\d+)", RegexOptions.Singleline)]
    private static partial Regex TestResultRegex();
}
