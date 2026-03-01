# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Is IAW

IAW (Interactive Agents Web) is an Orleans 10.0-based multi-agent runtime. Agents are Orleans grains implementing `IAgent` with durable journaled state. The system ships as NuGet packages and uses .NET Aspire for orchestration. All projects target `net11.0`.

## Build & Run Commands

```bash
# Run (always use aspire CLI — never dotnet run manually)
aspire run                                                          # start everything (default)
aspire run --project src/IAW.AppHost/Aspire.csproj                  # explicit AppHost path
aspire run --log-level debug                                        # verbose output for troubleshooting
aspire run --log-level trace                                        # maximum verbosity

# Build
dotnet build IAW.slnx                                               # build everything

# Test
dotnet test IAW.slnx                                                # run all tests
dotnet test test/Core.Tests/IAW.Core.Tests.csproj                   # AgentTest<Agent> behavior + scenario tests
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj     # Aspire integration tests only
dotnet test test/TelegramBot.Tests/TelegramBot.Tests.csproj         # TelegramBot model tests only
dotnet test IAW.slnx --filter "FullyQualifiedName~MethodName"       # run a single test by name
```

## Solution Structure

```
src/
  Core/Core.csproj                  -- Agent base class, grain impl, LLM integration, contracts
  IAW.AppHost/Aspire.csproj         -- Aspire orchestration (AppHost)
  IAW.MCP/MCP.csproj                -- MCP server bridge (stdio, self-contained exe)
  IAW.ServiceDefaults/              -- Shared Aspire defaults (OpenTelemetry, health checks)
  Clients.Telegram.Bot/             -- Telegram bot silo service
  DevUI/                            -- Microsoft Agent Framework dev UI
samples/
  Samples/Samples.csproj            -- Orleans silo with ~20 HTTP sample endpoints
  IAW.Testing/IAW.Testing.csproj     -- Testing framework: AgentTest<T>, AspireAgentTest<T>, ScenarioBuilder
test/
  Core.Tests/                       -- AgentTest<Agent> (41 universal behavior tests) + architecture guards
  Integration.Tests/                -- AspireAgentTest<Agent> cross-silo integration tests
  TelegramBot.Tests/                -- TelegramBot model unit tests
```

Central package management via `Directory.Packages.props` — all package versions are declared there.

## Architecture

### Agent Model — Two Layers

**OrleansAgentGrain** (durable, distributed): Extends `DurableGrain` (Orleans Journaling). All state uses `[Memory("name")]`-attributed `IDurableDictionary`/`IDurableList` parameters injected via constructor. Implements `IOrleansAgentGrain` which extends `IAgent`.

**Agent** (internal, in-memory): Non-Orleans agent class for standalone/test use. Supports `IChatClient` activation, streaming LLM responses via `SendAsync`, tools via `DefineTools()`, event handling via `HandleEventAsync`.

### IAgent — Composed Behavior Interfaces

`IAgent` extends `IGrainWithStringKey` plus 8 behavior interfaces defined in `IAgentBehaviors.cs`:

| Behavior | Purpose |
|----------|---------|
| `IAgentMetadataBehavior` | Agent identity and capabilities |
| `IAgentStateBehavior` | String key/value store + increment |
| `IAgentHistoryBehavior` | Conversation history + deterministic send |
| `IAgentEventsBehavior` | Event log (publish + query) |
| `IAgentNotificationsBehavior` | Pub/sub between agents |
| `IAgentTrackingBehavior` | Periodic timer/reminder execution |
| `IAgentConfigurationBehavior` | Runtime config (responses, tools, prompt prefix) |
| `IAgentToolsBehavior` | Tool invocation |
| `IAgentStreamsBehavior` | Orleans streaming |

### LLM Integration

Models are registered in `src/Core/AI/Models/` as singletons extending `LLMModel`. Each has a provider (Anthropic/OpenAI/Ollama) and a ServiceKey.

**Injection into grains** uses `[LlmAttribute<TModel>]` on a constructor parameter, which Orleans resolves via `LlmAttributeMapper<TModel>` to a keyed `IChatClient`.

**AppHost declaration**: `AddIAW("name").WithLLM<Sonnet46>()` registers models; `.WithLLMEnvironment(builder)` injects config + API key parameters into service projects.

**Provider registration**: `AddLlmProviders(this IHostApplicationBuilder)` in `LlmRegistration.cs` reads `AI:LLM:Models` config and registers `IChatClient` per model with OpenTelemetry wrapping.

### Aspire Hosting

`IAWExtensions.cs` provides:
- `AddIAW(name)` — creates Orleans resource with in-memory storage/streams/reminders
- `WithLLM<TModel>()` — declares which LLM models to use
- `WithLLMEnvironment()` — injects `AI__LLM__Models__*` env vars + API key secrets

Multi-silo setup: `samples` on ports 11111/30000, `telegram-bot` on 11112/30001, joined via `PrimarySiloEndpoint` config.

### Orleans Streaming

Stream provider named `"agents"`. Behavior streams use namespaces `"agent-events"`, `"agent-history"`, `"agent-notifications"` keyed by agent ID. Custom streams supported via `PublishStreamAsync(namespace, streamId, message)`.

### Serializable Contracts

All grain-to-grain types in `OrleansAgentContracts.cs` use `[GenerateSerializer]`. Notification envelopes support typed JSON payloads via `OrleansAgentNotificationJson` helpers.

## Test Patterns

**IAW.Testing package** (`src/IAW.Testing`): Ships `AgentTest<T>` and `AspireAgentTest<T>` base classes. Any class inheriting `AgentTest<T>` automatically gets 15 universal behavior tests covering all 8 IAgent behaviors. Includes a fluent `ScenarioBuilder` for multi-agent orchestration (`Given/When/Then`) and `MockChatClient` for LLM simulation.

**Unit tests** (`Core.Tests`): `CoreAgentTests : AgentTest<Agent>` — single line inherits all 15 behavior tests. `ScenarioBuilderTests` exercises the fluent scenario API. `ArchitectureGuardTests` validates design constraints via reflection. Total: 41 tests.

**Integration tests** (`Integration.Tests`): `OrleansAgentIntegrationTests : AspireAgentTest<Agent>` — boots full Aspire app, tests HTTP endpoints + direct Orleans client. Uses the same `Scenario` builder for cross-silo tests.

**Writing new agent tests**: Just inherit `AgentTest<YourAgent>` — all behaviors pass automatically. Add custom `[Fact]` methods for agent-specific logic.

## Code Style

- No `/// <summary>` XML doc comments. Only small inline comments in exceptional cases.
- Self-explanatory C# naming over documentation.
- All serializable Orleans types need `[GenerateSerializer]` and `[Id(n)]` attributes.
- Use `[Memory("name")]` attribute (alias for `FromKeyedServices`) for durable state injection.
