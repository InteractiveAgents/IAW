# Playwright Agent Design

**Date:** 2026-04-02
**Status:** Approved

## Goal

Add an LLM-driven browser automation agent to IAW that uses Playwright MCP to scrape, navigate, and extract data from web pages.

## Architecture

### Agent: `PlaywrightAgent : Agent<IPlaywright>`

**Namespace:** `IAW.Agents.Web`
**Location:** `src/Agents/Web/IPlaywright.cs` + `PlaywrightAgent.cs`

Follows the established MCP-client pattern from `AspireAgent`:

1. **On activation** — spawns `@playwright/mcp` via `StdioClientTransport` with `--headless` flag
2. **DefineTools()** — returns all Playwright MCP tools so the LLM can autonomously browse
3. **Interface methods** — `ScrapePage` and `ExtractData` for programmatic use by other agents
4. **On deactivation** — disposes MCP client (kills browser process)

### Interface: `IPlaywright : IAgent`

```csharp
public interface IPlaywright : IAgent
{
    static string IAgent.AgentDisplayName => "Playwright";
    static string IAgent.AgentDescription => "...";
    static string[] IAgent.AgentCapabilities => ["browser", "scrape", "web", "navigate", "extract"];
    static string IAgent.AgentInstructions => "...";

    Task<string> ScrapePageAsync(string url, string instructions, CancellationToken ct = default);
    Task<string> ExtractDataAsync(string url, string jsExpression, CancellationToken ct = default);
}
```

### MCP Connection

```csharp
_mcpClient = await McpClient.CreateAsync(
    new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "playwright",
        Command = "npx",
        Arguments = ["-y", "@playwright/mcp@latest", "--headless"],
    }),
    cancellationToken: ct);

_mcpTools = await _mcpClient.ListToolsAsync(cancellationToken: ct);
```

### How scraping works

When called via `GetResponse("Scrape the title and price from https://example.com/product")`:

1. LLM receives all Playwright MCP tools + the user instruction
2. LLM calls `browser_navigate` with the URL
3. LLM calls `browser_snapshot` to get the accessibility tree
4. LLM reads the tree and extracts requested data
5. For complex pages: LLM may use `browser_click`, `browser_evaluate`, or scroll
6. Returns structured text result

### Interface methods

- `ScrapePageAsync(url, instructions)` — navigates to URL, uses LLM to extract data per instructions
- `ExtractDataAsync(url, jsExpression)` — navigates to URL, runs JS expression, returns result directly (no LLM needed for simple extractions)

## Testing

### Unit test

`AgentTest<PlaywrightAgent>` with `MockChatClient` — verifies activation, tool registration, interface methods compile.

### Integration test via Aspire + IAW MCP

1. Start Aspire (`dotnet run --project src/IAW.AppHost`)
2. Use IAW MCP tools: `agent_send_message` to PlaywrightAgent
3. Test with real pages:
   - Simple: scrape title from a static page (e.g., example.com)
   - Medium: extract structured data from a real page (e.g., Hacker News front page titles)

## Dependencies

- `ModelContextProtocol` 1.1.0 (already in Directory.Packages.props)
- `@playwright/mcp` npm package (launched via npx at runtime)
- Node.js + npx available on PATH

## Files to create/modify

| File | Action |
|------|--------|
| `src/Agents/Web/IPlaywright.cs` | Create interface |
| `src/Agents/Web/PlaywrightAgent.cs` | Create implementation |
| `test/Core.Tests/PlaywrightAgentTests.cs` | Create unit tests |

No changes to Core, AppHost, or MCP server needed — agent auto-discovery handles registration.
