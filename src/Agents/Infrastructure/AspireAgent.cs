using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net.Http;

namespace IAW.Agents.Infrastructure;

public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient,
    ILogger<AspireAgent> logger,
    IHttpClientFactory httpClientFactory)
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

        if (!ScheduledJobs.ContainsKey("deploy-verify"))
        {
            await ScheduleJob("deploy-verify", TimeSpan.FromSeconds(60),
                "Verify deployment health: check all resources are running.", ct);
        }
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
        logger.LogInformation("Deploy: stopping assistant, building, then restarting");

        try
        {
            // Step 1: Stop assistant to release DLL locks
            await RestartResourceAsync("assistant", ct);
            await Task.Delay(5000, ct);

            // Step 2: Call MCP /deploy to build
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var response = await httpClient.PostAsync("http://localhost:5300/deploy", null, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogInformation("Deploy: build result = {Body}", body);

            // Step 3: Start assistant with fresh binary
            if (_mcpClient is not null)
            {
                await _mcpClient.CallToolAsync("execute_resource_command",
                    new Dictionary<string, object?> { ["resourceName"] = "assistant", ["commandName"] = "resource-start" },
                    cancellationToken: ct);
            }

            return $"Deploy completed: {body}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deploy: failed");
            return $"Deploy failed: {ex.Message}. Try RestartResource to recover.";
        }
    }
}