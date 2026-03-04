# Architecture

This page covers the V2 agent class hierarchy, durable state model, the `IAgentV2` interface, LLM integration, and observability infrastructure.

## Class Hierarchy

```
DurableGrain (Orleans.Journaling)
  └── AgentV2 (Core.V2)
        ├── implements IAgentV2
        └── implements IRemindable
```

`AgentV2` is a single flat base class that extends `DurableGrain` from `Microsoft.Orleans.Journaling`. It implements `IAgentV2` (the grain contract) and `IRemindable` (for scheduled reminders). There are no optional behavior interfaces to compose -- every `AgentV2` grain supports the full API surface.

`DurableGrain` provides journaled, transactional state persistence. All state mutations are committed via `WriteStateAsync()`.

## Durable State

The `AgentV2` constructor accepts six durable state collections, each annotated with `[Memory("name")]`. These are hidden from derived agents -- you never need to declare them yourself:

| Collection | Type | Storage Key | Purpose |
|---|---|---|---|
| messages | `IDurableList<AgentMessage>` | `v2-messages` | Conversation history (role, content, timestamp, metadata) |
| memory | `IDurableDictionary<string, string>` | `v2-memory` | Arbitrary key-value state |
| events | `IDurableList<AgentEvent>` | `v2-events` | Typed events with optional payload and metadata |
| subscriptions | `IDurableDictionary<string, List<string>>` | `v2-subscriptions` | Topic-to-subscriber mappings |
| notifications | `IDurableList<NotificationRecord>` | `v2-notifications` | Received notification records |
| tracking | `IDurableDictionary<string, string>` | `v2-tracking` | Schedule status (serialized JSON) |

All collections are backed by Orleans journaled grain storage, meaning they survive grain deactivation and silo restarts.

Derived agents have read access to `Messages`, `Memory`, and `Events` via protected properties.

## The IAgentV2 Interface

`IAgentV2` extends `IGrainWithStringKey` with a unified API surface:

```csharp
public interface IAgentV2 : IGrainWithStringKey
{
    Task<AgentProfile> GetProfileAsync(CancellationToken ct = default);

    Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default);

    Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query = null, CancellationToken ct = default);

    Task SetMemoryAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken ct = default);

    Task AppendEventAsync(AgentEvent agentEvent, CancellationToken ct = default);
    Task<List<AgentEvent>> QueryEventsAsync(AgentEventQuery? query = null, CancellationToken ct = default);

    Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
    Task NotifyAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task ReceiveNotificationAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task<List<NotificationRecord>> QueryNotificationsAsync(CancellationToken ct = default);

    Task StartScheduleAsync(TimeSpan interval, int? maxTicks = null, CancellationToken ct = default);
    Task StopScheduleAsync(CancellationToken ct = default);
    Task<ScheduleStatus> GetScheduleStatusAsync(CancellationToken ct = default);

    Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);

    Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
}
```

### Key Differences from V1

| V1 (IAgent) | V2 (IAgentV2) |
|---|---|
| 8 composed behavior interfaces | Single flat interface |
| `GetMetadataAsync()` returns `AgentMetadata` | `GetProfileAsync()` returns `AgentProfile` |
| `AddHistoryAsync(role, content)` | `AppendMessageAsync(AgentMessage)` with metadata |
| `GetHistoryAsync()` | `QueryMessagesAsync(AgentMessageQuery?)` with filtering |
| `PublishEventAsync(name, payload)` | `AppendEventAsync(AgentEvent)` with metadata |
| `GetEventsAsync()` | `QueryEventsAsync(AgentEventQuery?)` with filtering |
| `GetNotificationsAsync()` | `QueryNotificationsAsync()` |
| `StartTrackingAsync(interval, maxTicks)` | `StartScheduleAsync(interval, maxTicks?)` |
| `StopTrackingAsync()` | `StopScheduleAsync()` |
| `GetTrackingStatusAsync()` | `GetScheduleStatusAsync()` returns `ScheduleStatus` |
| `SetStateAsync(key, value)` | `SetMemoryAsync(key, value)` |
| `GetStateValueAsync(key)` | `GetMemoryAsync(key)` |

## LLM Integration

Models are registered in `src/Core/AI/Models/` as singletons extending `LLMModel`. Each has a provider (Anthropic, OpenAI, GitHub, Ollama) and a `ServiceKey`.

### Injection into Grains

Use `[Llm<TModel>]` on a constructor parameter. Orleans resolves this via `LlmAttributeMapper<TModel>` to a keyed `IChatClient`:

```csharp
public class MyAgent(
    // ... durable state (hidden in AgentV2) ...
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : AgentV2
{
    // Use chatClient in OnRespondAsync
}
```

### AppHost Declaration

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>();
```

### Provider Registration

`AddLlmProviders(this IHostApplicationBuilder)` in `LlmRegistration.cs` reads `AI:LLM:Models` configuration and registers `IChatClient` per model, wrapped with OpenTelemetry instrumentation.

### RespondWithLlmAsync

`AgentV2` provides a helper method that builds the full chat history, includes tools from `DefineTools()`, and calls `IChatClient.GetResponseAsync`:

```csharp
protected async Task<AgentReply> RespondWithLlmAsync(
    IChatClient chatClient,
    AgentRequest request,
    CancellationToken ct = default)
```

## Aspire Hosting

`IAWExtensions.cs` provides:
- `AddIAW(name)` -- creates Orleans resource with in-memory storage, streams, and reminders
- `WithLLM<TModel>()` -- declares which LLM models to use (auto-provisions Ollama containers)
- `WithOllama(configure)` -- customize Ollama with GPU support, data volumes, and OpenWebUI
- `WithLLMEnvironment()` -- injects `AI__LLM__Models__*` env vars + API key secrets

### Multi-Silo Topology

IAW supports multiple silos in the same cluster. Each silo uses distinct ports:

```csharp
// Silo 1: samples on ports 11111/30000
builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; })
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; });

// Silo 2: telegram-bot on ports 11112/30001
builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11112; })
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30001; });
```

Non-silo projects (like the MCP server or DevUI) connect as clients:

```csharp
builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient());
```

## Observability

The `AgentObservability` class provides built-in telemetry:

- **ActivitySource**: `"Core.Agent"` -- emits distributed traces for `agent.respond` and `agent.llm` operations
- **Meter**: `"Core.Agent"` -- exposes three counters:
  - `core.agent.sends` -- total respond operations
  - `core.agent.tool_calls` -- total tool invocations
  - `core.agent.failures` -- total failures during respond

Activities include `agent.id` and `agent.display_name` tags for filtering. All telemetry follows OpenTelemetry conventions and integrates with the .NET Aspire dashboard.
