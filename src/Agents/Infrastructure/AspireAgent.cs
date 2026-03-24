using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace IAW.Agents.Infrastructure;

public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient,
    ILogger<AspireAgent> logger)
    : Agent<IAspire>(durableState, chatClient), IAspire
{
    private McpClient? _mcpClient;
    private IList<McpClientTool> _mcpTools = [];

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await ConnectMcpAsync(ct);
        await base.OnActivateAsync(ct);

        if (!ScheduledJobs.ContainsKey("log-monitor"))
        {
            await ScheduleRecurringJob("log-monitor", TimeSpan.FromMinutes(30),
                "Check system health and report any resource errors or warnings.", ct);
        }

        // deploy-verify job is scheduled from DeployAsync, not on every activation
    }

    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        if (job.Name == "deploy-verify")
        {
            logger.LogInformation("Deploy verify: checking deployment health after restart");
            var resources = await ListResourcesAsync(ct);
            var healthy = resources.Contains("Running") && !resources.Contains("FailedToStart");

            if (!healthy)
            {
                logger.LogError("Deploy verify: UNHEALTHY after deployment!");
                await PublishAsync("deploy.verify.failed", new Dictionary<string, string>
                {
                    ["summary"] = "Deployment verification failed",
                    ["details"] = resources
                }, ct);
            }
            else
            {
                logger.LogInformation("Deploy verify: all resources healthy");
                await PublishAsync("deploy.verify.succeeded", new Dictionary<string, string>
                {
                    ["summary"] = "Deployment verified — all resources running"
                }, ct);
            }
            return;
        }

        if (job.Name == "log-monitor")
        {
            logger.LogInformation("Aspire log monitor: checking system health");
            var resources = await ListResourcesAsync(ct);
            if (resources.Contains("Stopped") || resources.Contains("FailedToStart"))
            {
                logger.LogWarning("Aspire log monitor: unhealthy resources detected");
                await PublishAsync("aspire.health.warning", new Dictionary<string, string>
                {
                    ["summary"] = "Unhealthy resources detected",
                    ["details"] = resources
                }, ct);
            }
            return;
        }

        await base.OnScheduledJobDueAsync(job, ct);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        if (_mcpClient is not null)
        {
            await _mcpClient.DisposeAsync();
            _mcpClient = null;
        }
        await base.OnDeactivateAsync(reason, ct);
    }

    protected override IReadOnlyList<AITool> DefineTools() => [.. _mcpTools];

    private async Task ConnectMcpAsync(CancellationToken ct)
    {
        try
        {
            var appHostPath = ResolveAppHostPath();
            if (appHostPath is null)
            {
                logger.LogWarning("Cannot resolve AppHost path — Aspire MCP tools unavailable");
                return;
            }

            _mcpClient = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "aspire",
                    Command = "aspire",
                    Arguments = ["mcp", "start", "--non-interactive"],
                    WorkingDirectory = appHostPath
                }),
                cancellationToken: ct);

            _mcpTools = await _mcpClient.ListToolsAsync(cancellationToken: ct);

            logger.LogInformation("Connected to Aspire MCP, loaded {ToolCount} tools", _mcpTools.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to Aspire MCP — agent will operate without tools");
            _mcpTools = [];
        }
    }

    private string? ResolveAppHostPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "IAW.AppHost");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    public async Task<string> RestartResourceAsync(string resourceName, CancellationToken ct = default)
    {
        if (_mcpClient is null) return "Aspire MCP not connected. Cannot manage resources.";
        try
        {
            await _mcpClient.CallToolAsync("execute_resource_command",
                new Dictionary<string, object?> { ["resourceName"] = resourceName, ["commandName"] = "resource-stop" },
                cancellationToken: ct);
            await Task.Delay(3000, ct);
            await _mcpClient.CallToolAsync("execute_resource_command",
                new Dictionary<string, object?> { ["resourceName"] = resourceName, ["commandName"] = "resource-start" },
                cancellationToken: ct);
            return $"Resource '{resourceName}' restarted successfully.";
        }
        catch (Exception ex)
        {
            return $"Failed to restart '{resourceName}': {ex.Message}";
        }
    }

    public async Task<string> ListResourcesAsync(CancellationToken ct = default)
    {
        if (_mcpClient is null) return "Aspire MCP not connected.";
        try
        {
            var result = await _mcpClient.CallToolAsync("list_resources", new Dictionary<string, object?>(),
                cancellationToken: ct);
            return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No resources found.";
        }
        catch (Exception ex) { return $"Failed to list resources: {ex.Message}"; }
    }

    public async Task<string> GetTracesAsync(string resourceName, CancellationToken ct = default)
    {
        if (_mcpClient is null) return "Aspire MCP not connected.";
        try
        {
            var result = await _mcpClient.CallToolAsync("list_traces",
                new Dictionary<string, object?> { ["resourceName"] = resourceName },
                cancellationToken: ct);
            return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No traces found.";
        }
        catch (Exception ex) { return $"Failed to get traces: {ex.Message}"; }
    }

    public async Task<string> GetLogsAsync(string resourceName, CancellationToken ct = default)
    {
        if (_mcpClient is null) return "Aspire MCP not connected.";
        try
        {
            var result = await _mcpClient.CallToolAsync("list_structured_logs",
                new Dictionary<string, object?> { ["resourceName"] = resourceName },
                cancellationToken: ct);
            return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "No logs found.";
        }
        catch (Exception ex) { return $"Failed to get logs: {ex.Message}"; }
    }

    public Task<string> CleanLogsAsync(string resourceName, CancellationToken ct = default)
    {
        return GetLogsAsync(resourceName, ct);
    }

    public async Task<string> DeployAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Deploy: building solution then restarting assistant");

        // build first — Aspire runs dotnet run --no-build on start, so pre-build is required
        var dotnetType = AgentInterfaceResolver.ResolveByDisplayName("DotNet");
        if (dotnetType is null) return "Deploy failed: DotNet agent not found.";

        var dotnet = (IAgent)GrainFactory.GetGrain(dotnetType, $"{this.GetPrimaryKeyString()}/{dotnetType.Name}");
        var buildResult = await dotnet.GetResponse(@"Build E:\IAW\IAW.slnx", ct);

        if (buildResult.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase))
            return $"Deploy aborted — build failed: {buildResult}";

        var restartResult = await RestartResourceAsync("assistant", ct);

        await ScheduleJob("deploy-verify", TimeSpan.FromMinutes(2),
            "Verify deployment health after restart", ct);

        return $"Deploy complete — build succeeded, {restartResult}";
    }
}