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
    [Llm<Claude45Haiku>] IChatClient chatClient,
    ILogger<AspireAgent> logger)
    : Agent(durableState, chatClient), IAspire
{
    private McpClient? _mcpClient;
    private IList<McpClientTool> _mcpTools = [];

    protected override string DisplayName => "Aspire";

    protected override string Instructions => """
        You are the Aspire infrastructure agent for the IAW system. You monitor and manage
        the running .NET Aspire application — its resources, health, logs, and traces.

        AVAILABLE MCP TOOLS:
        - list_resources: Get all running resources with state, health, endpoints
        - list_console_logs: View stdout from a resource (use for startup issues, crashes)
        - list_structured_logs: Search structured logs by resource (use for application errors)
        - list_traces: View distributed traces across resources (use for debugging request flows)
        - list_trace_structured_logs: Get logs for a specific trace ID
        - execute_resource_command: Restart, stop, or start resources
        - list_integrations / get_integration_docs: Aspire hosting integration reference
        - list_apphosts / select_apphost: Manage multiple AppHost sessions

        BEHAVIOR:
        1. Always start by gathering data with tools before answering
        2. NEVER dump raw tool output — summarize, filter, and highlight what matters
        3. For logs: surface errors and warnings first, skip info-level noise
        4. For traces: identify the failing span, show the error, suggest the cause
        5. For resource status: lead with unhealthy/degraded, then healthy as a brief list
        6. When multiple resources are involved, correlate — e.g., if telegram fails
           after assistant restart, say so
        7. If asked to restart/stop a resource, do it immediately — no confirmation needed
        8. Keep responses concise — this goes to Telegram where long messages are painful

        ERROR PATTERNS TO WATCH FOR:
        - Orleans serialization errors (CodecNotFoundException) — usually a missing [GenerateSerializer]
        - Telegram BotRequestException — usually MarkdownV2 escaping issues
        - MCP connection failures — AppHost may have restarted
        - Resource health flapping — repeated healthy/unhealthy transitions

        WHEN SOMETHING IS WRONG:
        - State the problem clearly in one sentence
        - Show the relevant error (just the exception type + message, not full stack)
        - Suggest the likely cause and fix
        - Offer to restart the resource if appropriate
        """;

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
        var workspace = GetWorkspacePath()
                        ?? Environment.GetEnvironmentVariable("IAW__Workspace");
        if (workspace is null) return null;

        var appHostDir = Path.Combine(workspace, "src", "IAW.AppHost");
        return Directory.Exists(appHostDir) ? appHostDir : null;
    }
}
