# AspireAgent MCP Redesign

## Problem

The `AspireAgent` exists but is non-functional. It uses `Process.Start("dotnet", ...)` to run CLI commands that don't connect to the running Aspire session. The IAW Telegram topic promises "check Aspire resource health, read logs and traces" but has no actual capability to do so. Meanwhile, a working demo (`demo/AspireGrain (1).cs`) shows the correct approach: connect to Aspire's MCP server via stdio transport and use its tools directly.

## Design

### Approach A — MCP tools only, clean IAspire

Rewrite `AspireAgent` to connect to the running Aspire session's MCP server on activation. Load all MCP tools dynamically. No custom typed methods — `IAgent.GetResponse()` is the interface. The agent's value comes from its specialized instructions that filter, summarize, and correlate telemetry data instead of dumping raw output.

### IAspire Interface

```csharp
public interface IAspire : IAgent { }
```

No typed methods. All communication goes through `GetResponse()`. CodeOrchestrator, Project agents, and scheduled jobs talk to it via natural language prompts. The MCP tools are an internal implementation detail.

### AspireAgent Activation & MCP Connection

`AspireAgent` overrides `OnActivateAsync`. It connects to MCP first, stores tools as a field, then calls `await base.OnActivateAsync(ct)` as the final step — which invokes `DefineTools()` and picks up the stored MCP tools. This matches the demo pattern.

1. Resolve AppHost path via `GetWorkspacePath()` from the base `Agent` class → `{workspace}/src/IAW.AppHost`. Fall back to `Environment.GetEnvironmentVariable("IAW__Workspace")` only if `GetWorkspacePath()` returns null.
2. Spawn MCP client via `McpClientFactory.CreateAsync` with `StdioClientTransport`:
   - Command: `aspire`
   - Arguments: `["mcp", "start", "--non-interactive"]`
   - WorkingDirectory: resolved AppHost path
3. Ping to verify connection
4. Load tools via `mcpClient.ListToolsAsync()` → store as `IList<McpClientTool>` field. `McpClientTool` inherits from `AITool`, so `DefineTools()` returns them directly — no conversion needed.
5. Call `await base.OnActivateAsync(ct)` — this calls `GetAllTools()` → `DefineTools()` which returns the stored MCP tools.

**MCP lifecycle:** The MCP connection is ephemeral and tied to grain activation lifetime. On deactivation, the MCP client (and its stdio process) is disposed. On reactivation, a fresh connection is established. If Aspire is no longer running at that point, the fallback path activates the agent without tools.

**Fallback:** If MCP connection fails (AppHost not running, aspire CLI missing, Aspire stopped between deactivation and reactivation), log warning and activate without tools. Agent tells callers "Aspire dashboard not available."

**Concurrency:** The grain is NOT reentrant. Concurrent `GetResponse()` calls are serialized, which is appropriate for an agent holding MCP state.

**Grain type:** Inherits `[GrainType("agent-v3")]` from the base `Agent` class — same as other infrastructure agents. `InterfaceCatalog` discovers it via the `IAspire` interface, not the grain type attribute.

### Instructions

```
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
```

### Integration Points

1. **IAW topic Project agent** — uses `Execute` → CodeOrchestrator → `IAspire.GetResponse("...")`. No changes to Project.cs needed. CodeOrchestrator's instructions need `IAspire ("aspire")` added to its available agents list.
2. **InterfaceCatalog** — discovers `IAspire` via interface scanning. Grain ID computed as `"aspire"` via kebab-case conversion.
3. **IAW MCP server** — `agent_send_message(agentId: "aspire", ...)` works out of the box since IAspire extends IAgent.
4. **NuGet** — `ModelContextProtocol` package added to `src/Agents/Agents.csproj` (version already pinned in `Directory.Packages.props`).

### Files Changed

| File | Change |
|------|--------|
| `src/Agents/Infrastructure/IAspire.cs` | Strip to `IAspire : IAgent { }`, remove `ResourceStatus`, `AspireMetrics` records |
| `src/Agents/Infrastructure/AspireAgent.cs` | Full rewrite: MCP client connection, `DefineTools()` override, new instructions |
| `src/Agents/Agents.csproj` | Add `ModelContextProtocol` package reference (no version — centrally managed) |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Add `IAspire ("aspire")` to available agents in instructions |

### Files Unchanged

- `Project.cs` — no Aspire tools hardcoded into any topic
- `StreamSubscriber.cs` — no new streams (future Approach C)
- `Agent.cs` base class — no modifications

## Future Evolution (Approach C)

Once proven, the AspireAgent can evolve to proactive monitoring:
- Periodic health polling via Orleans reminders (same pattern as scheduled jobs)
- Publish `resource.unhealthy`, `resource.restarted` events to Orleans streams
- IAW and Notifications topics receive alerts automatically
- Throttle to avoid notification spam
