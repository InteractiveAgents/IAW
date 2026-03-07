using System.Diagnostics;
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using IAW.Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Infrastructure;

public class ShellAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<IAW.Core.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IShell
{
    protected override string DisplayName => "Shell Agent";
    protected override string Instructions =>
        "You are a shell command agent. You execute shell commands and dotnet CLI operations. " +
        "You track execution metrics, failure rates, and command frequency.";

    public async Task<CommandResult> ExecuteAsync(
        string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var effectiveDirectory = workingDirectory ?? GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var sw = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = effectiveDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new CommandResult(-1, "", "Failed to start process", sw.Elapsed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            sw.Stop();

            var result = new CommandResult(process.ExitCode, output, error, sw.Elapsed);
            await RecordCommandExecution(command, result, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            sw.Stop();

            var result = new CommandResult(-1, "", "Command timed out", sw.Elapsed);
            await RecordCommandExecution(command, result, ct);
            return result;
        }
    }

    public async Task<CommandResult> RunDotnetAsync(
        string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var effectiveDirectory = workingDirectory ?? GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var sw = Stopwatch.StartNew();

        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = effectiveDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new CommandResult(-1, "", "Failed to start dotnet process", sw.Elapsed);
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        sw.Stop();

        var result = new CommandResult(process.ExitCode, output, error, sw.Elapsed);
        await RecordCommandExecution($"dotnet {arguments}", result, ct);
        return result;
    }

    public Task<ShellMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalCommands = GetCounterValue("total-commands");
        var failedCommands = GetCounterValue("failed-commands");
        var commandFrequency = DeserializeDictionary("command-frequency");
        var totalDurationMs = GetLongValue("total-duration-ms");
        var avgExecutionTime = totalCommands > 0
            ? TimeSpan.FromMilliseconds(totalDurationMs / totalCommands)
            : TimeSpan.Zero;

        return Task.FromResult(new ShellMetrics(totalCommands, failedCommands, commandFrequency, avgExecutionTime));
    }

    private async Task RecordCommandExecution(string command, CommandResult result, CancellationToken ct)
    {
        IncrementCounter("total-commands");
        if (result.ExitCode != 0)
            IncrementCounter("failed-commands");

        var totalDurationMs = GetLongValue("total-duration-ms") + (long)result.Duration.TotalMilliseconds;
        State["total-duration-ms"] = new StateEntry("total-duration-ms", totalDurationMs);

        var commandKey = ExtractCommandName(command);
        var frequency = DeserializeDictionary("command-frequency");
        frequency.TryGetValue(commandKey, out var currentCount);
        frequency[commandKey] = currentCount + 1;
        State["command-frequency"] = new StateEntry("command-frequency", JsonSerializer.Serialize(frequency));

        await WriteStateAsync(ct);

        var eventName = result.ExitCode == 0 ? "command.completed" : "command.failed";
        await PublishAsync(eventName, new Dictionary<string, object>
        {
            ["Command"] = command,
            ["ExitCode"] = result.ExitCode,
            ["DurationMs"] = (long)result.Duration.TotalMilliseconds
        }, ct);
    }

    private static string ExtractCommandName(string command)
    {
        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
    }

    private void IncrementCounter(string counterKey)
    {
        var current = GetCounterValue(counterKey);
        State[counterKey] = new StateEntry(counterKey, current + 1);
    }

    private int GetCounterValue(string counterKey)
    {
        if (!State.TryGetValue(counterKey, out var desc)) return 0;
        return desc.Value is int i ? i : int.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private long GetLongValue(string key)
    {
        if (!State.TryGetValue(key, out var desc)) return 0;
        return desc.Value is long l ? l : long.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private Dictionary<string, int> DeserializeDictionary(string key)
    {
        if (!State.TryGetValue(key, out var desc))
            return new Dictionary<string, int>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(desc.Value.ToString()!)
                   ?? new Dictionary<string, int>();
        }
        catch
        {
            return new Dictionary<string, int>();
        }
    }
}
