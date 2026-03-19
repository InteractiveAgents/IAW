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
dotnet run --project src/IAW.AppHost/Aspire.csproj        # run via Aspire orchestrator
```

CI runs on `windows-latest` with .NET 11.0 preview SDK. The `global.json` pins SDK version `11.0.100-preview.1.26104.118`.

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
| `Agent.Tools.cs` | AI tool registration and invocation |
| `Agent.Tracking.cs` | Periodic monitoring via Orleans reminders |
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
| `src/Agents` (IAW.Agents) | 65 agent implementations (infrastructure, LLM wrappers, memory, orchestration) | Yes |
| `src/Agents.CSharp` (IAW.Agents.CSharp) | Roslyn, DotNet, GitHub, NuGet agents | Yes |
| `src/Aspire.Hosting.IAW` | AppHost integration: `AddIAW()`, `IAWService`, `WithLLM<T>()`, `WithReference(iaw)` | Yes |
| `src/Aspire.IAW.Client` | Service integration: silo `AddIAW()`, client `AddIAWClient()`, OTel, health | Yes |
| `src/IAW.Testing` (IAW.Testing) | `AgentTest<TAgent>` base class with TestCluster, MockChatClient | Yes |
| `src/IAW.AppHost` | Aspire AppHost — defines distributed topology via `Aspire.Hosting.IAW` | No |
| `src/IAW.Assistant` | Production silo hosting all agents (single `builder.AddIAW()` call) | No |
| `src/IAW.MCP` | MCP server bridge (localhost:5300) for Claude Code | No |
| `src/DevUI` | Blazor web UI for agent interaction | No |
| `src/Clients.Telegram` | Telegram bot client with Ngrok tunneling | No |

### Aspire Hosting (`src/IAW.AppHost`)

`builder.AddIAW("iaw")` returns `IAWService` which chains `.WithLLM<T>()`, `.WithOllama()`, `.WithVoice2Text<T>()`, `.WithStorage()`, `.WithVectorDb()`. `.WithReference(iaw)` on a project auto-propagates Orleans membership, API keys, model config, blob/qdrant connections, and WaitFor dependencies. No separate `WithLLMEnvironment()` needed.

Key ports: assistant silo on 30000 (gateway) / 11111 (silo), MCP on 5300.

### Testing (`src/IAW.Testing`)

Inherit from `AgentTest<TAgent>` — it spins up a `TestCluster` with memory storage, mock LLM (`MockChatClient` returning `"mock-response"`), and all model mappers registered. Use `Agent(UniqueId("prefix"))` to get grain references with unique IDs per test run. Tests use xunit.v3 with `TestContext.Current.CancellationToken`.

### Code Orchestration (`src/Agents/Orchestration`)

The Project agent delegates complex tasks to `CodeOrchestratorAgent` via the `Execute` tool. The orchestrator:

1. Receives a natural-language plan
2. Generates a standalone C# console app that connects to the cluster as an Orleans client (`builder.AddIAWClient()`)
3. The generated code calls agent grains directly via `client.GetGrain<IAgent>("grain-id").GetResponse()`
4. Executes the project with `dotnet run`, captures output, returns `result.json`

Agent grain IDs are computed from interface names by `InterfaceCatalog.ComputeGrainId()` — strips leading "I", inserts "-" at lowercase→uppercase transitions, lowercases. Example: `ISonnet46` → `sonnet46`, `IGpt4oMini` → `gpt4o-mini`.

### LLM Model Comparison

The Project agent has a `CompareModelsTool` that sends the same prompt to multiple LLM wrapper agents in parallel. Each LLM agent (e.g. `Gpt54MiniAgent`, `Sonnet46Agent`) wraps a specific model via `[Llm<TModel>]`. The tool collects response text, wall-clock duration, and token usage (`GetLastUsage()`) for side-by-side comparison. Results include a metrics table and full responses. Traces are visible in Aspire with `gen_ai.*` attributes.

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

## MCP Integration

`.mcp.json` configures three MCP servers: `iaw` (localhost:5300), `aspire` (CLI), `context7` (npm). The IAW MCP server in `src/IAW.MCP` exposes agent tools: `agent_list_all`, `assistant_chat`, `agent_send_message`, `agent_get_status`, `agent_assign_task`, `agent_get_events`, `agent_get_metrics`, `agent_trigger_self_improvement`.
