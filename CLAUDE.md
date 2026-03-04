# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What Is IAW

IAW (Interactive Agents Web) is an Orleans-based multi-agent runtime for .NET. Agents are Orleans grains extending `AgentV2` with durable journaled state. The system ships as NuGet packages and uses .NET Aspire for orchestration. All projects target `net11.0`.

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
dotnet test test/Core.Tests/IAW.Core.Tests.csproj                   # AgentTestV2<Agent> behavior + scenario tests
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj     # Aspire integration tests only
dotnet test IAW.slnx --filter "FullyQualifiedName~MethodName"       # run a single test by name
```

## Solution Structure

```
src/
  Core/Core.csproj                  -- Agent base class (AgentV2), V2 contracts, LLM integration
  IAW.AppHost/Aspire.csproj         -- Aspire orchestration (AppHost)
  IAW.MCP/MCP.csproj                -- MCP server bridge (stdio, self-contained exe)
  IAW.ServiceDefaults/              -- Shared Aspire defaults (OpenTelemetry, health checks)
  Clients.Telegram.Bot/             -- Telegram bot silo service
  DevUI/                            -- Microsoft Agent Framework dev UI
samples/
  Samples/Samples.csproj            -- Orleans silo with ~20 HTTP sample endpoints
  IAW.Testing/IAW.Testing.csproj    -- Testing framework: AgentTestV2<T>, AspireAgentTest<T>, ScenarioBuilder
test/
  Core.Tests/                       -- AgentTestV2<Agent> behavior tests + architecture guards
  Integration.Tests/                -- AspireAgentTest<Agent> cross-silo integration tests
```

Central package management via `Directory.Packages.props` -- all package versions are declared there.

## Architecture

### Agent Model (V2)

**AgentV2** (durable, distributed): Extends `DurableGrain` (Orleans Journaling). All state (`messages`, `memory`, `events`, `subscriptions`, `notifications`, `tracking`) is managed internally by the base class. Derived agents do NOT pass `[Memory]` constructor parameters -- they only override `Profile` and `OnRespondAsync`.

**IAgentV2** -- flat interface (no composed behavior interfaces):
- `GetProfileAsync` -- agent identity, capabilities, instructions
- `RespondAsync` -- send a request, get a reply
- `AppendMessageAsync` / `QueryMessagesAsync` -- conversation history
- `SetMemoryAsync` / `GetMemoryAsync` -- key/value memory store
- `AppendEventAsync` / `QueryEventsAsync` -- event log
- `SubscribeAsync` / `NotifyAsync` / `ReceiveNotificationAsync` / `QueryNotificationsAsync` -- pub/sub
- `StartScheduleAsync` / `StopScheduleAsync` / `GetScheduleStatusAsync` -- timers/reminders
- `PublishStreamAsync` -- Orleans streaming
- `InvokeToolAsync` -- tool invocation

**Agent (V1 shim)**: The original `Agent` class now extends `AgentV2` for backward compatibility. `IAgent` extends `IAgentV2`.

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

Multi-silo setup: `samples` on ports 11111/30000, `telegram-bot` on 11112/30001, joined via `PrimarySiloEndpoint` config.

### Orleans Streaming

Stream provider named `"agents"`. Behavior streams use namespaces `"agent-events"`, `"agent-history"`, `"agent-notifications"` keyed by agent ID. Custom streams supported via `PublishStreamAsync(namespace, streamId, message)`.

### Serializable Contracts

All grain-to-grain types use `[GenerateSerializer]` and `[Id(n)]` attributes. V2 contracts live in `src/Core/V2/`: `AgentProfile`, `AgentRequest`, `AgentReply`, `AgentMessage`, `AgentEvent`, `ScheduleStatus`, etc.

## Test Patterns

**IAW.Testing package** (`samples/IAW.Testing`): Ships `AgentTestV2<T>` and `AspireAgentTest<T>` base classes. Any class inheriting `AgentTestV2<T>` automatically gets 16 universal behavior tests covering all IAgentV2 behaviors. Includes a fluent `ScenarioBuilder` for multi-agent orchestration (`Given/When/Then`) and `MockChatClient` for LLM simulation.

**Unit tests** (`test/Core.Tests`): `CoreAgentTests : AgentTestV2<Agent>` -- single line inherits all behavior tests. `ScenarioBuilderTests` exercises the fluent scenario API. `ArchitectureGuardTests` validates design constraints via reflection.

**Integration tests** (`test/Integration.Tests`): `OrleansAgentIntegrationTests : AspireAgentTest<Agent>` -- boots full Aspire app, tests HTTP endpoints + direct Orleans client. Uses the same `Scenario` builder for cross-silo tests.

**Writing new agent tests**: Just inherit `AgentTestV2<YourAgent>` -- all behaviors pass automatically. Add custom `[Fact]` methods for agent-specific logic.

## Code Style

- No `/// <summary>` XML doc comments. Only small inline comments in exceptional cases.
- Self-explanatory C# naming over documentation.
- All serializable Orleans types need `[GenerateSerializer]` and `[Id(n)]` attributes.
