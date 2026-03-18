# AspireAgent MCP Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite AspireAgent to connect to the running Aspire MCP server, giving it live access to resource health, logs, traces, and commands.

**Architecture:** AspireAgent connects to Aspire's MCP server via stdio transport (`aspire mcp start --non-interactive`) during grain activation. MCP tools are loaded dynamically and returned from `DefineTools()`. The `IAspire` interface is stripped to just `IAgent` — all interaction goes through `GetResponse()`.

**Tech Stack:** .NET 11, Orleans, ModelContextProtocol 1.1.0, Microsoft.Extensions.AI

**Spec:** `docs/superpowers/specs/2026-03-18-aspire-agent-mcp-redesign.md`

---

### Task 1: Strip IAspire interface

**Files:**
- Modify: `src/Agents/Infrastructure/IAspire.cs`

- [ ] **Step 1: Replace IAspire.cs contents**

Remove all typed methods and serializable records. Replace with:

```csharp
using Core.Contracts;

namespace IAW.Agents.Infrastructure;

public interface IAspire : IAgent { }
```

- [ ] **Step 2: Verify no consumers of removed methods**

Run: `grep -r "ListResourcesAsync\|RestartResourceAsync\|StopResourceAsync\|StartResourceAsync\|GetLogsAsync\|GetMetricsAsync\|ResourceStatus\|AspireMetrics" src/ --include="*.cs" -l`

Expected: only `src/Agents/Infrastructure/IAspire.cs` and `src/Agents/Infrastructure/AspireAgent.cs` — no external consumers.

- [ ] **Step 3: Build to confirm no breakage**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Infrastructure/IAspire.cs
git commit -m "refactor: strip IAspire to marker interface"
```

---

### Task 2: Add ModelContextProtocol package

**Files:**
- Modify: `src/Agents/Agents.csproj`

- [ ] **Step 1: Add package reference**

Add to the `<ItemGroup>` in `src/Agents/Agents.csproj`:

```xml
<PackageReference Include="ModelContextProtocol" />
```

No version needed — centrally managed in `Directory.Packages.props` (version 1.1.0).

- [ ] **Step 2: Verify version is pinned in Directory.Packages.props**

Run: `grep ModelContextProtocol Directory.Packages.props`
Expected: `<PackageVersion Include="ModelContextProtocol" Version="1.1.0" />`

- [ ] **Step 3: Restore and build**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Agents.csproj
git commit -m "chore: add ModelContextProtocol package to Agents"
```

---

### Task 3: Rewrite AspireAgent with MCP connection

**Files:**
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`

- [ ] **Step 1: Rewrite AspireAgent.cs**

Replace the entire file with the MCP-based implementation. Key points:
- Override `OnActivateAsync`: connect MCP client via `McpClient.CreateAsync` with `StdioClientTransport`, store tools, then call `base.OnActivateAsync(ct)`.
- Override `DefineTools()`: return stored MCP tools (or empty if connection failed).
- Override `OnDeactivateAsync`: dispose MCP client.
- Resolve AppHost path via `GetWorkspacePath()` with env var fallback.
- Graceful fallback: if MCP fails, log warning, activate without tools.
- NOTE: Do NOT implement `IAsyncDisposable` — Orleans grains use `OnDeactivateAsync` for cleanup.
- NOTE: Verify correct `using` statements at build time — `McpClient`, `StdioClientTransport`, `StdioClientTransportOptions`, `McpClientTool` may need additional namespaces beyond `ModelContextProtocol.Client`.

```csharp
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace IAW.Agents.Infrastructure;

public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    ILogger<AspireAgent> logger)
    : Agent(durableState, chatClient), IAspire
{
    private IMcpClient? _mcpClient;
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
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Build the full solution to check for downstream breakage**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Infrastructure/AspireAgent.cs
git commit -m "feat: rewrite AspireAgent with Aspire MCP connection"
```

---

### Task 4: Add IAspire to CodeOrchestrator's available agents

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs:52-65`

- [ ] **Step 1: Add IAspire to the agent interfaces and available agents list**

In the `Instructions` string, modify the two relevant sections:

Change line 52:
```
- Agent interfaces: `Core.Contracts` (IAgent), `IAW.Agents.Infrastructure` (IShell, IFileSystem, IBuild, IGit)
```
to:
```
- Agent interfaces: `Core.Contracts` (IAgent), `IAW.Agents.Infrastructure` (IShell, IFileSystem, IBuild, IGit, IAspire)
```

Add after line 65 (`- IReviewer ("reviewer"): code review via GetResponse`):
```
- IAspire ("aspire"): Aspire infrastructure monitoring — resource health, logs, traces, restart/stop commands via GetResponse
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "feat: add IAspire to CodeOrchestrator available agents"
```

---

### Task 5: Integration test — verify end-to-end via Aspire

**Files:**
- None created — manual verification against running Aspire session.

- [ ] **Step 1: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run existing unit tests**

Run: `dotnet test test/Core.Tests --verbosity quiet`
Expected: No new failures introduced (existing FormTests failures are pre-existing).

- [ ] **Step 3: Start Aspire and verify AspireAgent activation**

Start Aspire: `cd src/IAW.AppHost && dotnet run`
Wait for all resources to be Running.

Check assistant structured logs for: "Connected to Aspire MCP, loaded {N} tools"
If fallback: "Failed to connect to Aspire MCP" — check that the `aspire` CLI is available and AppHost path resolves.

- [ ] **Step 4: Test via IAW MCP tools**

Use `agent_send_message(agentId: "aspire", message: "list all resources and their health status")`.
Expected: Agent responds with a summarized resource list (not raw JSON dump).

- [ ] **Step 5: Test via Telegram IAW topic**

Send a message in the IAW Telegram topic: "what's the system status?"
Expected: Project agent calls Execute → CodeOrchestrator → IAspire.GetResponse → summarized system health.

- [ ] **Step 6: Commit any final adjustments (only if code changes were needed)**

Stage only the specific files that were adjusted — do not use `git add -A`.
