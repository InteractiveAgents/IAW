using System.Diagnostics;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Tools;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Infrastructure;

public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IAspire
{
    protected override string DisplayName => "Aspire";
    protected override string Instructions => """
        You are Aspire, the IAW team's orchestration and deployment infrastructure specialist.
        You manage .NET Aspire resources — listing, starting, stopping, and restarting services.
        You have RunShellAsync and RunDotnetAsync tools — use them to execute Aspire CLI commands.
        When asked about resource health or service management, execute the operation immediately.
        Report resource status, health, and any errors concisely.
        """;

    protected override IReadOnlyList<AITool> DefineTools()
    {
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new ShellTools(() => GetWorkspacePath() ?? Directory.GetCurrentDirectory()));
        return tools;
    }

    public async Task<ResourceStatus[]> ListResourcesAsync(CancellationToken ct = default)
    {
        var appHostPath = GetAppHostPath();
        var result = await RunDotnetAsync($"run --project \"{appHostPath}\" -- --list", ct);

        State["last-health-check"] = new StateEntry("last-health-check", DateTimeOffset.UtcNow.ToString("O"));
        await WriteStateAsync(ct);

        return ParseResourceOutput(result.Output);
    }

    public async Task RestartResourceAsync(string resourceName, CancellationToken ct = default)
    {
        await StopResourceAsync(resourceName, ct);
        await StartResourceAsync(resourceName, ct);

        IncrementCounter("total-restarts");
        IncrementResourceRestartCount(resourceName);
        await WriteStateAsync(ct);

        await PublishAsync("resource.restarted", new Dictionary<string, object>
        {
            ["ResourceName"] = resourceName
        }, ct);
    }

    public async Task StopResourceAsync(string resourceName, CancellationToken ct = default)
    {
        var appHostPath = GetAppHostPath();
        await RunDotnetAsync($"run --project \"{appHostPath}\" -- --stop {resourceName}", ct);
        RemoveResourceUptime(resourceName);
        await WriteStateAsync(ct);
    }

    public async Task StartResourceAsync(string resourceName, CancellationToken ct = default)
    {
        var appHostPath = GetAppHostPath();
        var result = await RunDotnetAsync($"run --project \"{appHostPath}\" -- --start {resourceName}", ct);

        SetResourceStartTime(resourceName);
        await WriteStateAsync(ct);

        var eventName = result.ExitCode == 0 ? "resource.healthy" : "resource.failed";
        await PublishAsync(eventName, new Dictionary<string, object>
        {
            ["ResourceName"] = resourceName,
            ["ExitCode"] = result.ExitCode
        }, ct);
    }

    public async Task<string[]> GetLogsAsync(string resourceName, CancellationToken ct = default)
    {
        var appHostPath = GetAppHostPath();
        var result = await RunDotnetAsync($"run --project \"{appHostPath}\" -- --logs {resourceName}", ct);
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public Task<AspireMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalRestarts = GetCounterValue("total-restarts");
        var restartCounts = DeserializeDictionaryInt("restart-counts");
        var resourceUptime = CalculateResourceUptimes();
        var lastHealthCheck = State.TryGetValue("last-health-check", out var lastDesc)
            ? DateTimeOffset.Parse(lastDesc.Value.ToString()!)
            : DateTimeOffset.MinValue;

        return Task.FromResult(new AspireMetrics(totalRestarts, restartCounts, resourceUptime, lastHealthCheck));
    }

    private string GetAppHostPath()
    {
        if (State.TryGetValue("apphost-path", out var desc))
            return desc.Value.ToString()!;

        var workspace = GetWorkspacePath();
        if (workspace is not null)
        {
            var candidates = Directory.GetFiles(workspace, "*.csproj", SearchOption.AllDirectories)
                .Where(f => f.Contains("AppHost", StringComparison.OrdinalIgnoreCase)
                         || f.Contains("Aspire", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Length > 0)
                return candidates[0];
        }

        return "src/Aspire/Aspire.csproj";
    }

    private async Task<(string Output, int ExitCode)> RunDotnetAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var workspace = GetWorkspacePath();
        if (workspace is not null)
            psi.WorkingDirectory = workspace;

        using var process = Process.Start(psi);
        if (process is null)
            return ("Failed to start dotnet process", -1);

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var combined = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
        return (combined, process.ExitCode);
    }

    private static ResourceStatus[] ParseResourceOutput(string output)
    {
        var resources = new List<ResourceStatus>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length >= 3)
            {
                var endpoints = parts.Length >= 4
                    ? parts[3].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    : [];
                resources.Add(new ResourceStatus(parts[0], parts[1], parts[2], endpoints));
            }
        }
        return [.. resources];
    }

    private void IncrementResourceRestartCount(string resourceName)
    {
        var counts = DeserializeDictionaryInt("restart-counts");
        counts.TryGetValue(resourceName, out var current);
        counts[resourceName] = current + 1;
        State["restart-counts"] = new StateEntry("restart-counts", JsonSerializer.Serialize(counts));
    }

    private void SetResourceStartTime(string resourceName)
    {
        var uptimes = DeserializeDictionaryString("resource-start-times");
        uptimes[resourceName] = DateTimeOffset.UtcNow.ToString("O");
        State["resource-start-times"] = new StateEntry("resource-start-times", JsonSerializer.Serialize(uptimes));
    }

    private void RemoveResourceUptime(string resourceName)
    {
        var uptimes = DeserializeDictionaryString("resource-start-times");
        uptimes.Remove(resourceName);
        State["resource-start-times"] = new StateEntry("resource-start-times", JsonSerializer.Serialize(uptimes));
    }

    private Dictionary<string, TimeSpan> CalculateResourceUptimes()
    {
        var startTimes = DeserializeDictionaryString("resource-start-times");
        var now = DateTimeOffset.UtcNow;
        var uptimes = new Dictionary<string, TimeSpan>();
        foreach (var (resource, startTimeStr) in startTimes)
        {
            if (DateTimeOffset.TryParse(startTimeStr, out var startTime))
                uptimes[resource] = now - startTime;
        }
        return uptimes;
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

    private Dictionary<string, int> DeserializeDictionaryInt(string key)
    {
        if (!State.TryGetValue(key, out var desc))
            return new Dictionary<string, int>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(desc.Value.ToString()!)
                   ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }

    private Dictionary<string, string> DeserializeDictionaryString(string key)
    {
        if (!State.TryGetValue(key, out var desc))
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(desc.Value.ToString()!)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
