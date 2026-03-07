using System.Diagnostics;
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using IAW.Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Infrastructure;

public class GitAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<IAW.Core.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IGit
{
    protected override string DisplayName => "Git Agent";
    protected override string Instructions =>
        "You are a git agent. You manage git operations including status, commits, diffs, log, and reverts. " +
        "You track commit patterns and file churn metrics.";

    public async Task<string> StatusAsync(string repoPath, CancellationToken ct = default)
        => (await RunGitAsync("status", repoPath, ct)).Output;

    public async Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default)
    {
        await RunGitAsync("add -A", repoPath, ct);

        var result = await RunGitAsync($"commit -m \"{message.Replace("\"", "\\\"")}\"", repoPath, ct);

        IncrementCounter("total-commits");
        State["last-commit"] = new StateEntry("last-commit", DateTimeOffset.UtcNow.ToString("O"));
        await UpdateFileChurn(repoPath, ct);
        await WriteStateAsync(ct);

        await PublishAsync("commit.created", new Dictionary<string, object>
        {
            ["RepoPath"] = repoPath,
            ["Message"] = message
        }, ct);

        return result.Output;
    }

    public async Task<string> DiffAsync(string repoPath, CancellationToken ct = default)
        => (await RunGitAsync("diff", repoPath, ct)).Output;

    public async Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default)
    {
        var result = await RunGitAsync($"log --oneline -n {count}", repoPath, ct);
        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default)
    {
        var result = await RunGitAsync($"revert --no-edit {commitHash}", repoPath, ct);

        IncrementCounter("total-reverts");
        await WriteStateAsync(ct);

        await PublishAsync("revert.completed", new Dictionary<string, object>
        {
            ["RepoPath"] = repoPath,
            ["CommitHash"] = commitHash
        }, ct);

        return result.Output;
    }

    public Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalCommits = GetCounterValue("total-commits");
        var totalReverts = GetCounterValue("total-reverts");
        var fileChurn = DeserializeDictionary("file-churn");
        var lastCommit = State.TryGetValue("last-commit", out var lastDesc)
            ? DateTimeOffset.Parse(lastDesc.Value.ToString()!)
            : DateTimeOffset.MinValue;

        return Task.FromResult(new GitMetrics(totalCommits, totalReverts, fileChurn, lastCommit));
    }

    private async Task<(string Output, int ExitCode)> RunGitAsync(
        string arguments, string repoPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return ("Failed to start git process", -1);

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var combined = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
        return (combined, process.ExitCode);
    }

    private async Task UpdateFileChurn(string repoPath, CancellationToken ct)
    {
        var result = await RunGitAsync("diff --name-only HEAD~1 HEAD", repoPath, ct);
        if (result.ExitCode != 0) return;

        var churn = DeserializeDictionary("file-churn");
        foreach (var file in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            churn.TryGetValue(file, out var currentCount);
            churn[file] = currentCount + 1;
        }
        State["file-churn"] = new StateEntry("file-churn", JsonSerializer.Serialize(churn));
    }

    private void IncrementCounter(string key)
    {
        var current = GetCounterValue(key);
        State[key] = new StateEntry(key, current + 1);
    }

    private int GetCounterValue(string key)
    {
        if (!State.TryGetValue(key, out var desc)) return 0;
        return desc.Value is int i ? i : int.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
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
