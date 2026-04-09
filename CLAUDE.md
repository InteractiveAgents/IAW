# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
dotnet build IAW.slnx                                    # build everything
dotnet test IAW.slnx                                     # run all tests
dotnet test test/Core.Tests                               # run core unit tests only
dotnet test test/Integration.Tests                        # run integration tests only
dotnet test --filter "FullyQualifiedName~AgentBasicTests"  # run a single test class
dotnet test --filter "FullyQualifiedName~GetResponse_ReturnsLlmResponse"  # single test
dotnet run --project src/Aspire/Aspire.csproj              # run via Aspire orchestrator
```

CI runs on `windows-latest` with .NET 11.0 preview SDK. The `global.json` pins SDK version `11.0.100-preview.1.26104.118`.

### Testing strategy — prefer behavioral tests over unit tests

`dotnet test` handles unit and integration regressions, but it uses `MockChatClient` returning `"mock-response"` and cannot exercise end-to-end agent reasoning, memory recall, approval gating, or orchestration. For any change that touches agent behavior, LLM prompts, tools, context providers, approval flows, or memory, **you MUST drive the live system via the `iaw` MCP server instead of relying on `dotnet test` alone**. The `iaw` MCP server exposes:

- `assistant_chat` — send a message to a user's Thread agent and observe the full response, tool calls, and streamed output. This is the primary way to verify agent behavior end-to-end.
- `agent_send_message` — talk to a specific agent by grain id; useful for driving a sub-agent directly without routing through Thread.
- `agent_list_all` — enumerate active agents and their capabilities to confirm registration/removal.
- `agent_get_status` — inspect a grain's current state, last response, usage metrics.
- `agent_get_events` — read the durable event log for an agent; use this to verify events like `ToolDenied`, `ApprovalRequested`, `MemoryStored` fired when expected.

Behavioral verification loop for any non-trivial change:

1. `dotnet build IAW.slnx` — clean compile.
2. Start Aspire via the aspire MCP and confirm every resource is Healthy.
3. Drive the scenario through the `iaw` MCP — e.g. `assistant_chat` a prompt that triggers the new code path. Read back the response, observe which tools were called, observe the event log via `agent_get_events`.
4. Cross-check with Aspire traces (`mcp__aspire__list_traces`, `list_structured_logs`) to confirm the expected spans and log lines appear.
5. Only after the behavior is verified live, run `dotnet test` for regression safety.

If you find yourself reaching for `dotnet test` first for a feature change, stop and start with the `iaw` MCP instead. Unit tests with mocks catch regressions; they do not prove the feature works.

## Architecture

### Orleans Agent Framework

Every agent is an **Orleans grain** inheriting from `Agent` (abstract, `[GrainType("agent-v3")]`) in `src/Core/Agents/Agent.cs`. The base class is split across partial files:

| File | Responsibility |
|------|---------------|
| `Agent.cs` | Core: activation, LLM streaming, response handling, context enrichment |
| `Agent.Events.cs` | Typed event publishing to Orleans streams |
| `Agent.Lifecycle.cs` | Activation hooks, reminder management, deactivation |
| `Agent.State.cs` | Durable state (history, key-value dict, event log) via `AgentDurableState` |
| `Agent.Streams.cs` | Auto-subscribe to streams based on `IStreamConsumer<T>` interfaces |
| `Agent.Tools.cs` | AI tool registration and invocation; interface methods auto-register as AI tools |
| `Agent.Scheduling.cs` | Periodic monitoring and scheduled work via Orleans reminders |
| `Agent.Observers.cs` | Stream observer pattern |

**Durable state** uses Orleans Journaling (`DurableGrain` + `IDurableList`/`IDurableDictionary`), not classic `[Persistent]` state.

### Key Patterns

- **Constructor injection via attributes**: `[AgentState]` injects `AgentDurableState`, `[Llm<TModel>]` injects model-specific `IChatClient`
- **Communication**: Three patterns — direct `IAgent.GetResponse()` calls, typed P2P via `IReceiver<TMessage>`, pub/sub via `IStreamProducer<T>`/`IStreamConsumer<T>` over Orleans streams (provider name: `"agents"`)
- **Context enrichment**: Agents override `GetContextProviders()` to inject memory/project/task context into prompts before LLM calls
- **History management**: `DurableChatHistoryProvider` auto-summarizes at 40 messages via `HistorySummarizer`

### Project Layout

| Project | Purpose | Packable |
|---------|---------|----------|
| `src/Core` (IAW.Core) | Agent base class, contracts, AI integration, tools, observability | Yes |
| `src/Agents` (IAW.Agents) | Agent implementations grouped into namespaces: System, Coding, Models, Memory, Orchestration | Yes |
| `src/Agents.CSharp` (IAW.Agents.CSharp) | Roslyn, DotNet, GitHub, NuGet agents | Yes |
| `src/Aspire.Hosting` (Aspire.Hosting.IAW) | AppHost integration: `AddIAW()`, `IAWService`, `WithLLM<T>()`, `WithReference(iaw)` | Yes |
| `src/Aspire.Client` (Aspire.IAW.Client) | Service integration: silo `AddIAW()`, client `AddIAWClient()`, OTel, health | Yes |
| `src/Testing` (IAW.Testing) | `AgentTest<TAgent>` base class with TestCluster, MockChatClient | Yes |
| `src/Aspire` | Aspire AppHost — defines distributed topology via `Aspire.Hosting.IAW` | No |
| `src/Agents.Host` | Production silo hosting all agents (single `builder.AddIAW()` call) | No |
| `src/MCP` | MCP server bridge (localhost:5300) for Claude Code | No |
| `src/DevUI` | Blazor web UI for agent interaction | No |
| `src/Telegram` | Telegram bot client with Ngrok tunneling | No |

### Aspire Hosting (`src/Aspire`)

`builder.AddIAW("iaw")` returns `IAWService` which chains `.WithLLM<T>()`, `.WithOllama()`, `.WithVoice2Text<T>()`, `.WithStorage()`, `.WithVectorDb()`. `.WithReference(iaw)` on a project auto-propagates Orleans membership, API keys, model config, blob/qdrant connections, and WaitFor dependencies. No separate `WithLLMEnvironment()` needed.

Key ports: assistant silo on 30000 (gateway) / 11111 (silo), MCP on 5300.

### Testing (`src/Testing`)

Inherit from `AgentTest<TAgent>` — it spins up a `TestCluster` with memory storage, mock LLM (`MockChatClient` returning `"mock-response"`), and all model mappers registered. Use `Agent(UniqueId("prefix"))` to get grain references with unique IDs per test run. Tests use xunit.v3 with `TestContext.Current.CancellationToken`.

### Code Orchestration (`src/Agents/Orchestration`)

The Thread agent (`IThread`) delegates complex tasks to `CodeOrchestratorAgent` via the `Execute` tool. The orchestrator:

1. Receives a natural-language plan
2. Generates a standalone C# console app that connects to the cluster as an Orleans client (`builder.AddIAWClient()`)
3. The generated code retrieves agent grains via `client.Get<IGit>(taskId).GetResponse()` — `Get<T>()` resolves agent IDs via `AgentRegistry` keyed to the current task context
4. Executes the project with `dotnet run`, captures output, returns `result.json`

`AgentRegistry` maps interface types to running grain instances. The `ComputeGrainId()` helper has been removed; grain resolution is dynamic through `Get<T>(taskId)`.

Model comparison is handled via orchestration rather than a dedicated `CompareModelsTool`. Each LLM agent (e.g. `Gpt54MiniAgent`, `Sonnet46Agent`) wraps a specific model via `[Llm<TModel>]`; the orchestrator can fan out calls and collect token usage (`GetLastUsage()`) with traces visible in Aspire under `gen_ai.*` attributes.

### Default LLM Model

The first model in the AppHost `WithLLM<T>()` chain becomes the default (non-keyed) `IChatClient`. Agents without `[Llm<T>]` use this default. Only agents needing a specific model (like ShellAgent with Haiku, or LLM wrapper agents) use `[Llm<T>]`.

### Observability

OpenTelemetry with activity source `"IAW"` and meter `"IAW"`. Metrics: `Activations`, `MessagesSent`, `ConversationErrors`, `ConversationDuration`, `TokenUsage`, `TotalInputTokens`, `TotalOutputTokens`. Gen AI semantic conventions on trace spans (`gen_ai.agent.id`, `gen_ai.usage.input_tokens`, etc.).

## Code Style

- **No** default `/// <summary>` comments — only small inline comments in exceptional cases
- Self-explanatory C# naming over comments
- `TreatWarningsAsErrors` is enabled globally (suppressed: `ORLEANSEXP005`)
- C# `LangVersion` is `preview` (latest features)
- Centralized package versioning in `Directory.Packages.props`

## LSP Servers

Configured in `.lsp.json` — gives Claude Code real-time diagnostics, go-to-definition, and code intelligence.

- **csharp** (`csharp-ls`) — C# language server, uses `IAW.slnx`.
- **typescript** (`typescript-language-server`) — TypeScript/JavaScript for the `website/` project.

### LSP Setup

Fully automatic — no manual steps. `dotnet tool restore` installs `csharp-ls`. `npm install` in `website/` installs `typescript-language-server`. `.lsp.json` uses `dotnet tool run` and `npx` wrappers.

## MCP Servers

Configured in `.mcp.json`:
- **iaw** — IAW agent MCP server (localhost:5300) exposing agent tools: `agent_list_all`, `assistant_chat`, `agent_send_message`, `agent_get_status`, `agent_get_events`.
- **aspire** — Aspire dashboard MCP for monitoring resources, logs, traces, and metrics.
- **context7** — Library documentation search.
- **microsoft-learn** — Official Microsoft/Azure documentation search and fetch.
- **playwright** — Browser automation for simulating user activity.
- **chrome-devtools** — Chrome DevTools protocol for debugging and testing.
- **stitch** — UI design and screen generation.

### Context7 Usage

**ALWAYS use Context7 to look up package/framework APIs before writing any code or dispatching any subagent.** No exceptions — every API must be verified via Context7 first.

1. Resolve the library ID: `mcp__context7__resolve-library-id` (e.g. "orleans", "aspire", "openai-dotnet").
2. Query the docs: `mcp__context7__query-docs` with the resolved ID and your topic.
3. Only then write code based on verified API signatures.

This prevents stale training-data assumptions from producing incorrect code.

## Verification Flow (Post-Implementation)

After making any changes, follow this full verification flow before returning results. **Agent behavior is verified by talking to the running system through the `iaw` MCP, not by `dotnet test`.**

### 1. Build & Start
```bash
dotnet build IAW.slnx
```
Then use the aspire MCP to start the application and confirm every resource is Healthy:
- `mcp__aspire__select_apphost` → `mcp__aspire__list_resources` — every service in a Running state.

### 2. Drive the scenario via the `iaw` MCP
This is the primary behavioral gate. For the feature or fix you just implemented:
- `mcp__iaw__assistant_chat` — send a realistic user prompt that exercises the changed path and observe the full response, tool calls, and streaming.
- `mcp__iaw__agent_send_message` — if you want to bypass routing and hit a specific sub-agent directly, call it by grain id.
- `mcp__iaw__agent_get_events` — read the durable event log of the involved agents; confirm the events you expected (`ToolDenied`, `ApprovalRequested`, `MemoryStored`, `JobCompleted`, etc.) actually fired and contain the right payload keys.
- `mcp__iaw__agent_get_status` — sanity-check the agent's state, last response, and usage metrics.
- `mcp__iaw__agent_list_all` — confirm the registry reflects any agent additions or removals.

If the change affects UI rendering or the end-user Telegram/DevUI experience, follow up with the playwright MCP for the visible-surface checks. Playwright is UX verification; `iaw` MCP is behavioral verification — both are needed when the change spans both layers.

### 3. Cross-check telemetry (Aspire MCP)
After driving the scenario, confirm the telemetry matches your mental model:
- **Traces**: `mcp__aspire__list_traces` — verify `gen_ai.*` spans appear with the expected agent ids, token counts, tool names.
- **Structured logs**: `mcp__aspire__list_structured_logs` — check for log lines you expected (approval judgments, memory store/recall, tool errors).
- **Trace drill-down**: `mcp__aspire__list_trace_structured_logs` — inspect specific traces end to end when a behavior looks off.

### 4. Regression safety via `dotnet test`
Run `dotnet test test/Core.Tests` and `dotnet test test/Integration.Tests` to catch regressions in pure-logic paths that don't require the live system. Treat these as a safety net, not as proof that the feature works.

### 5. Return results
Only after (1) build clean, (2) live IAW MCP scenario passes, (3) telemetry confirms expected spans/logs, (4) `dotnet test` green — return the result to the user. If any step fails, debug and fix before proceeding; do not claim a change is done based on `dotnet test` alone.
