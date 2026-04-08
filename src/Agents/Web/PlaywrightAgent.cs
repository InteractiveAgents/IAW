using Core.AI;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace IAW.Agents.Web;

public class PlaywrightAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Balanced>] IChatClient chatClient,
    ILogger<PlaywrightAgent> logger)
    : Agent<IPlaywright>(durableState, chatClient), IPlaywright
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

    public async Task<string> ScrapePageAsync(string url, string instructions, CancellationToken ct = default)
    {
        var prompt = $"Navigate to {url} and extract the following: {instructions}";
        return await GetResponse(prompt, ct);
    }

    public async Task<string> ExtractDataAsync(string url, string jsExpression, CancellationToken ct = default)
    {
        if (_mcpClient is null)
            return "Playwright MCP not connected. Cannot extract data.";

        try
        {
            await _mcpClient.CallToolAsync("browser_navigate",
                new Dictionary<string, object?> { ["url"] = url },
                cancellationToken: ct);

            var result = await _mcpClient.CallToolAsync("browser_evaluate",
                new Dictionary<string, object?> { ["function"] = $"() => {{ return {jsExpression}; }}" },
                cancellationToken: ct);

            return result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
                ?? "No result from evaluation.";
        }
        catch (Exception ex)
        {
            return $"Failed to extract data: {ex.Message}";
        }
    }

    private async Task ConnectMcpAsync(CancellationToken ct)
    {
        try
        {
            _mcpClient = await McpClient.CreateAsync(
                new StdioClientTransport(new StdioClientTransportOptions
                {
                    Name = "playwright",
                    Command = "npx",
                    Arguments = ["-y", "@playwright/mcp@latest", "--headless"],
                }),
                cancellationToken: ct);

            _mcpTools = await _mcpClient.ListToolsAsync(cancellationToken: ct);

            logger.LogInformation("Connected to Playwright MCP, loaded {ToolCount} tools", _mcpTools.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to connect to Playwright MCP — agent will operate without browser tools");
            _mcpTools = [];
        }
    }
}
