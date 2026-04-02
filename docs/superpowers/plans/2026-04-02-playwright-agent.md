# Playwright Agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an LLM-driven browser automation agent that uses Playwright MCP to scrape and extract data from web pages.

**Architecture:** Orleans grain `PlaywrightAgent : Agent<IPlaywright>` that spawns `@playwright/mcp` via `StdioClientTransport` on activation, exposes all Playwright MCP tools to the LLM via `DefineTools()`, and provides typed interface methods (`ScrapePageAsync`, `ExtractDataAsync`) for programmatic use by other agents. Follows the established `AspireAgent` MCP-client pattern exactly.

**Tech Stack:** Orleans 9.x, Microsoft.Extensions.AI (IChatClient, AITool), ModelContextProtocol 1.1.0 (McpClient, StdioClientTransport), @playwright/mcp (npm, launched via npx), xunit.v3

---

### Task 1: Create IPlaywright Interface

**Files:**
- Create: `src/Agents/Web/IPlaywright.cs`

- [ ] **Step 1: Create the interface file**

```csharp
using Core.Contracts;
using System.ComponentModel;

namespace IAW.Agents.Web;

public interface IPlaywright : IAgent
{
    static string IAgent.AgentDisplayName => "Playwright";

    static string IAgent.AgentDescription =>
        "Automates browser interactions — navigates pages, scrapes content, extracts structured data using Playwright MCP tools.";

    static string[] IAgent.AgentCapabilities =>
        ["browser", "scrape", "web", "navigate", "extract", "screenshot", "automation"];

    static string IAgent.AgentInstructions => """
        You are Playwright, the browser automation specialist. You navigate web pages, scrape content,
        and extract structured data using browser tools.

        RULES:
        - ALWAYS call browser_navigate first to load a page before any other browser action.
        - Use browser_snapshot to read page content via the accessibility tree — this is your primary way to "see" the page.
        - Use browser_evaluate to run JavaScript for precise data extraction when the accessibility tree is insufficient.
        - Use browser_click and browser_fill to interact with dynamic pages (pagination, forms, dropdowns).
        - Return extracted data in a clear, structured format (lists, tables, key-value pairs).
        - DO NOT attempt to navigate to login-protected pages without explicit credentials.
        - DO NOT scrape pages faster than one request per 2 seconds to be respectful.
        - If a page fails to load or times out, report the error clearly — do not retry silently.
        """;

    [Description("Navigate to a URL, read the page using browser tools, and extract data according to the given instructions. Returns the extracted content as structured text.")]
    Task<string> ScrapePageAsync(string url, string instructions, CancellationToken ct = default);

    [Description("Navigate to a URL and evaluate a JavaScript expression to extract data directly. Returns the JS evaluation result as a string.")]
    Task<string> ExtractDataAsync(string url, string jsExpression, CancellationToken ct = default);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Web/IPlaywright.cs
git commit -m "feat: add IPlaywright interface for browser automation agent"
```

---

### Task 2: Create PlaywrightAgent Implementation

**Files:**
- Create: `src/Agents/Web/PlaywrightAgent.cs`

- [ ] **Step 1: Create the agent implementation**

```csharp
using Core;
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
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Web/PlaywrightAgent.cs
git commit -m "feat: add PlaywrightAgent — LLM-driven browser automation via Playwright MCP"
```

---

### Task 3: Create Unit Tests

**Files:**
- Create: `test/Core.Tests/PlaywrightAgentTests.cs`

These tests use the `TestCluster` with `MockChatClient` — no real browser is spawned. They verify the agent activates, responds, and exposes correct metadata. The MCP connection will gracefully fail in test (npx not wired in TestCluster), so we test the agent's fallback behavior.

- [ ] **Step 1: Write the test file**

```csharp
using Core.Contracts;
using IAW.Agents.Web;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class PlaywrightAgentBasicTests : AgentTest<PlaywrightAgent>
{
    [Fact]
    public async Task GetMetadata_ReturnsPlaywrightDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("pw-meta"));
        var metadata = await agent.GetMetadata(ct);
        Assert.Equal("Playwright", metadata.DisplayName);
    }

    [Fact]
    public async Task GetMetadata_HasBrowserCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("pw-caps"));
        var metadata = await agent.GetMetadata(ct);
        Assert.Contains("browser", metadata.Capabilities);
        Assert.Contains("scrape", metadata.Capabilities);
        Assert.Contains("web", metadata.Capabilities);
    }

    [Fact]
    public async Task GetResponse_WithoutMcp_StillResponds()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("pw-resp"));
        var response = await agent.GetResponse("Hello", ct);
        Assert.Equal("mock-response", response);
    }

    [Fact]
    public async Task ScrapePageAsync_WithoutMcp_ReturnsLlmResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IPlaywright>(UniqueId("pw-scrape"));
        var result = await grain.ScrapePageAsync("https://example.com", "get the title", ct);
        Assert.Equal("mock-response", result);
    }

    [Fact]
    public async Task ExtractDataAsync_WithoutMcp_ReturnsErrorMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IPlaywright>(UniqueId("pw-extract"));
        var result = await grain.ExtractDataAsync("https://example.com", "document.title", ct);
        Assert.Contains("not connected", result);
    }

    [Fact]
    public async Task GetHistory_AfterScrape_ContainsMessages()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IPlaywright>(UniqueId("pw-hist"));
        await grain.ScrapePageAsync("https://example.com", "get the title", ct);
        var history = await ((IAgent)grain).GetHistory(ct);
        Assert.NotEmpty(history);
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~PlaywrightAgent" -v m`
Expected: All 6 tests pass

- [ ] **Step 3: Commit**

```bash
git add test/Core.Tests/PlaywrightAgentTests.cs
git commit -m "test: add PlaywrightAgent unit tests"
```

---

### Task 4: Integration Test via Aspire + IAW MCP

This task starts the full Aspire stack and tests the PlaywrightAgent end-to-end via the IAW MCP tools. Run manually — not in CI.

- [ ] **Step 1: Build the solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 2: Start Aspire**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Wait for all resources to show Running in the Aspire dashboard.

- [ ] **Step 3: Verify Playwright agent is registered**

Use IAW MCP tool `agent_list_all`. Confirm `Playwright` appears in the list with capabilities `["browser", "scrape", "web", ...]`.

- [ ] **Step 4: Test simple scrape — example.com**

Use IAW MCP tool `agent_send_message`:
- agentId: `IPlaywright`
- message: `Navigate to https://example.com and tell me what the page title and main heading are.`

Expected: Response mentions "Example Domain" as the heading.

- [ ] **Step 5: Test structured scrape — Hacker News**

Use IAW MCP tool `agent_send_message`:
- agentId: `IPlaywright`  
- message: `Navigate to https://news.ycombinator.com and extract the top 5 story titles as a numbered list.`

Expected: Response contains 5 numbered story titles from the HN front page.

- [ ] **Step 6: Test ExtractData — JS evaluation**

Use IAW MCP tool `agent_send_message`:
- agentId: `IPlaywright`
- message: `Use ExtractData to get document.title from https://example.com`

Expected: Response contains "Example Domain".

- [ ] **Step 7: Commit final state**

```bash
git add -A
git commit -m "feat: PlaywrightAgent — browser automation via Playwright MCP"
```
