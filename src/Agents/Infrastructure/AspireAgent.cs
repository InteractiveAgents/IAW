using Core.AI;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace IAW.Agents.Infrastructure;

public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    ILogger<AspireAgent> logger)
    : Agent<IAspire>(durableState, chatClient), IAspire
{
    private McpClient? _mcpClient;
    private IList<McpClientTool> _mcpTools = [];

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await ConnectMcpAsync(ct);
        await base.OnActivateAsync(ct);
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
}
