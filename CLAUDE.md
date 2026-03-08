# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Is IAW

IAW (Interactive Agents Web) is an Orleans-based multi-agent runtime for .NET. Agents are Orleans grains extending `Agent` with durable journaled state. The system ships as NuGet packages and uses .NET Aspire for orchestration. All projects target `net11.0`.

## Build & Run Commands

```bash
# Run (always use aspire CLI -- never dotnet run manually)
aspire run                                                          # start everything (default)
aspire run --project src/IAW.AppHost/Aspire.csproj                  # explicit AppHost path
aspire run --log-level debug                                        # verbose output for troubleshooting
aspire run --log-level trace                                        # maximum verbosity

# Build
dotnet build IAW.slnx                                               # build everything

# Test
dotnet test IAW.slnx                                                # run all tests
dotnet test test/Core.Tests/IAW.Core.Tests.csproj                   # AgentTest<T> behavior + architecture guard tests
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj     # Aspire integration tests only
dotnet test IAW.slnx --filter "FullyQualifiedName~MethodName"       # run a single test by name
```

## Solution Structure

```
src/
  Core/Core.csproj                  -- Agent base class, models, sessions, context providers
  Agents/Agents.csproj              -- 14 out-of-the-box agents (Infrastructure, Orchestration, Review, Knowledge)
  Agents.CSharp/Agents.CSharp.csproj -- 4 C# development agents (Roslyn, DotNet, NuGet, GitHub)
  IAW.AppHost/Aspire.csproj         -- Aspire orchestration (AppHost)
  IAW.MCP/MCP.csproj                -- MCP server bridge (Orleans client, HTTP transport)
  IAW.ServiceDefaults/              -- Shared Aspire defaults (OpenTelemetry, health checks)
  IAW.Testing/IAW.Testing.csproj    -- Testing framework: AgentTest<T>, AspireAgentTest<T>, ScenarioBuilder
  Clients.Telegram.Bot/             -- Telegram bot (Orleans silo, ports 11112/30001)
  DevUI/                            -- Microsoft Agent Framework DevUI (Orleans client -> IAgent grains)
samples/
  Samples/Samples.csproj            -- Primary Orleans silo (ports 11111/30000), sample endpoints
test/
  Core.Tests/                       -- AgentTest<T> behavior tests + architecture guards
  Integration.Tests/                -- AspireAgentTest<T> cross-silo integration tests
```

Central package management via `Directory.Packages.props` -- all package versions are declared there.

## NuGet Packages

| Package | Purpose |
|---------|---------|
| IAW.Core | Agent base class, models, sessions, context providers |
| IAW.Agents | 14 out-of-the-box agents (FileSystem, Shell, Git, PersonalAssistant, etc.) |
| IAW.Agents.CSharp | 4 Roslyn-powered C# agents (Roslyn, DotNet, NuGet, GitHub) |
| IAW.Testing | AgentTest<T> + universal contract tests |

## Architecture

### Agent Model

**Agent** (durable, distributed): Extends `DurableGrain` (Orleans Journaling). All state (`messages`, `memory`, `events`, `subscriptions`, `notifications`, `tracking`) is managed internally by the base class. Derived agents pass `[Memory]` constructor parameters and override `Instructions`, `DisplayName`, and optionally `DefineTools()`.

**IAgent** -- flat interface (no composed behavior interfaces):
- `GetMetadata` -- agent identity, capabilities
- `GetResponse` / `GetResponseStream` -- conversation
- `GetHistory` / `ClearHistory` -- conversation history
- `GetState` / `SetWorkspace` -- state management
- `GetCapabilities` -- agent capabilities
- `HandleEvent` -- event handling
- `GetEventLog` -- event log
- `PublishToStream` / `GetActiveSubscriptions` -- streaming
- `Cancel` -- lifecycle

### LLM Integration

Models are registered in `src/Core/AI/Models/` as singletons extending `LLMModel`. Each has a provider (Anthropic/OpenAI/Ollama) and a ServiceKey.

**Injection into grains** uses `[LlmAttribute<TModel>]` on a constructor parameter, which Orleans resolves via `LlmAttributeMapper<TModel>` to a keyed `IChatClient`.

**AppHost declaration**: `AddIAW("name").WithLLM<Sonnet46>()` registers models; `.WithLLMEnvironment(builder)` injects config + API key parameters into service projects.

**Provider registration**: `AddLlmProviders(this IHostApplicationBuilder)` in `LlmRegistration.cs` reads `AI:LLM:Models` config and registers `IChatClient` per model with OpenTelemetry wrapping.

### Aspire Hosting

`IAWExtensions.cs` provides:
- `AddIAW(name)` -- creates Orleans resource with in-memory storage/streams/reminders
- `WithLLM<TModel>()` -- declares which LLM models to use
- `WithLLMEnvironment()` -- injects `AI__LLM__Models__*` env vars + API key secrets

### DevUI (Microsoft Agent Framework)

DevUI provides a web-based chat UI for interacting with Orleans agents. It runs as an **Orleans client** (not a silo) connecting to the `samples` silo via gateway.

### MCP Server

`IAW.MCP` runs as an Orleans client connecting to `samples` silo. Exposes 8 orchestration tools via MCP HTTP transport: `agent_list_all`, `assistant_chat`, `agent_send_message`, `agent_get_status`, `agent_assign_task`, `agent_get_events`, `agent_get_metrics`, `agent_trigger_self_improvement`.

### Orleans Streaming

Stream provider named `"agents"`. Behavior streams use namespaces `"agent-events"`, `"agent-history"`, `"agent-notifications"` keyed by agent ID.

### Serializable Contracts

All grain-to-grain types use `[GenerateSerializer]` and `[Id(n)]` attributes. Contracts live in `src/Core/`: `AgentEvent`, `AgentMetadata`, `AgentCapabilities`, `AgentState`, `StateEntry`, `ChatMessage`, etc.

## Test Patterns

**IAW.Testing package** (`src/IAW.Testing`): Ships `AgentTest<T>` and `AspireAgentTest<T>` base classes. Any class inheriting `AgentTest<T>` automatically gets 18 universal behavior tests. Includes `MockChatClient` for LLM simulation.

**Unit tests** (`test/Core.Tests`): 18 agent test classes (one per agent) inheriting `AgentTest<T>`. `ArchitectureGuardTests` validates design constraints via reflection.

**Integration tests** (`test/Integration.Tests`): `OrleansAgentIntegrationTests : AspireAgentTest<Agent>` -- boots full Aspire app, tests HTTP endpoints + direct Orleans client.

**Writing new agent tests**: Just inherit `AgentTest<YourAgent>` -- all behaviors pass automatically. Add custom `[Fact]` methods for agent-specific logic.

## Code Style

- No `/// <summary>` XML doc comments. Only small inline comments in exceptional cases.
- Self-explanatory C# naming over documentation.
- All serializable Orleans types need `[GenerateSerializer]` and `[Id(n)]` attributes.
