# V3 Public API Surface

Audit date: 2026-03-07
Source: `src/Core/V3/`

## Core Types

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `Agent` | abstract class | `Core.V3` | Base agent grain. Primary constructors inject 5 `[Memory]` durable stores + `IChatClient`. Partial class split across 9 files. |
| `DynamicAgent` | class | `Core.V3` | Runtime-configurable agent. Extends `Agent`, implements `IDynamicAgent`. |
| `IAgent` | interface | `Core.V3` | Primary grain interface. 11 methods: conversation, state, metadata, events, streams, lifecycle. |
| `IDynamicAgent` | interface | `Core.V3` | Extends `IAgent` with `ConfigureAsync`. |
| `IEventDrivenAgent` | interface | `Core.V3` | Marker interface (no additional methods). |
| `IStreamingAgent` | interface | `Core.V3` | Marker interface (no additional methods). |
| `ITrackableAgent` | interface | `Core.V3` | Adds `StartTrackingAsync` / `StopTrackingAsync`. |
| `IObservableAgent` | interface | `Core.V3` | Adds `SubscribeObserverAsync` / `UnsubscribeObserverAsync`. |

## Serializable Records (all have `[GenerateSerializer]`)

| Type | Namespace | Id range | Purpose |
|------|-----------|----------|---------|
| `AgentEvent` | `Core.V3` | 0-4 | Domain event with string payload dictionary. |
| `AgentState` | `Core.V3` | 0 | Snapshot of all state entries. |
| `AgentMetadata` | `Core.V3` | 0-6 | Agent identity, kind, capabilities, pub/sub. |
| `AgentCapabilities` | `Core.V3` | 0-7 | Feature flags (memory, P2P, events, tools, etc). |
| `AgentResponse` | `Core.V3` | 0-3 | LLM response with kind, content, optional metadata. |
| `AgentConfiguration` | `Core.V3` | 0-4 | Dynamic agent configuration payload. |
| `ChatMessage` | `Core.V3` | 0-2 | Durable chat history entry. |
| `StateEntry` | `Core.V3` | 0-1 | Key-value state entry. |
| `TrackingItem` | `Core.V3` | 0-5 | Scheduled monitoring item. |
| `ToolDescription` | `Core.V3` | 0-1 | Tool name + description pair. |

## Enums

| Type | Namespace | Serializable | Purpose |
|------|-----------|-------------|---------|
| `AgentKind` | `Core.V3` | Yes | `Static` / `Dynamic` |
| `AgentResponseKind` | `Core.V3` | No | `Text` / `ToolCall` / `ToolResult` / `Error` / `Final` |

## Communication Types

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `BroadcastResult` | record | `Core.V3.Communication` | Result of a broadcast (delivered/failed counts). Id 0-3. |
| `MessageReceipt` | record | `Core.V3.Communication` | Receipt for P2P message delivery. Id 0-3. |
| `IAgentObserver<TEvent>` | interface | `Core.V3.Communication` | Grain observer for typed notifications. |
| `IBroadcaster<TMessage>` | interface | `Core.V3.Communication` | One-to-many message broadcast. |
| `INotifier<TNotification>` | interface | `Core.V3.Communication` | Observer-pattern notifications. |
| `IReceiver<TMessage>` | interface | `Core.V3.Communication` | Point-to-point message receiver. |
| `IStreamConsumer<TEvent>` | interface | `Core.V3.Communication` | Orleans stream consumer marker. |
| `IStreamProducer<TEvent>` | interface | `Core.V3.Communication` | Orleans stream producer marker. |

## Message Types (all have `[GenerateSerializer]`)

| Type | Kind | Namespace | Base Interface | Id range |
|------|------|-----------|----------------|----------|
| `IAgentMessage` | interface | `Core.V3.Messages` | - | - |
| `IEvent` | interface | `Core.V3.Messages` | `IAgentMessage` | - |
| `ICommand` | interface | `Core.V3.Messages` | `IAgentMessage` | - |
| `INotification` | interface | `Core.V3.Messages` | `IAgentMessage` | - |
| `AgentActivatedEvent` | record | `Core.V3.Messages` | `IEvent` | 0-3 |
| `AlertNotification` | record | `Core.V3.Messages` | `INotification` | 0-4 |
| `AssignTaskCommand` | record | `Core.V3.Messages` | `ICommand` | 0-4 |
| `BuildCompletedEvent` | record | `Core.V3.Messages` | `IEvent` | 0-5 |
| `CodeChangedEvent` | record | `Core.V3.Messages` | `IEvent` | 0-4 |
| `DeployCompletedEvent` | record | `Core.V3.Messages` | `IEvent` | 0-5 |
| `HealthCheckEvent` | record | `Core.V3.Messages` | `IEvent` | 0-5 |
| `ProgressNotification` | record | `Core.V3.Messages` | `INotification` | 0-5 |
| `ReviewRequestNotification` | record | `Core.V3.Messages` | `INotification` | 0-4 |
| `StateChangedEvent` | record | `Core.V3.Messages` | `IEvent` | 0-5 |
| `TestResultEvent` | record | `Core.V3.Messages` | `IEvent` | 0-6 |

## Attributes

| Type | Namespace | Purpose |
|------|-----------|---------|
| `CapabilityAttribute` | `Core.V3.Attributes` | Declares agent capabilities via `[Capability("name")]`. |
| `PublishesAttribute` | `Core.V3.Attributes` | Declares published event names via `[Publishes("name")]`. |
| `SubscribesAttribute` | `Core.V3.Attributes` | Declares subscribed event names via `[Subscribes("name")]`. |

## Context

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `AIContext` | record | `Core.V3.Context` | Additional messages + metadata for context injection. Id 0-1. |
| `IAIContextProvider` | interface | `Core.V3.Context` | Provide/store context around agent conversations. |

## Diagnostics

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `DiagnosticReport` | record | `Core.V3.Diagnostics` | Health report with event/message counts, uptime, issues. Id 0-6. |
| `ISelfDiagnosable` | interface | `Core.V3.Diagnostics` | Agents that can self-diagnose. |

## Observability

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `AgentTelemetry` | static class | `Core.V3.Observability` | OpenTelemetry counters, histograms, activity source. |

## Registry

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `AgentRegistryGrain` | class | `Core.V3.Registry` | Durable registry of all agent types. `[GrainType("agent-registry")]`. |
| `IAgentRegistryGrain` | interface | `Core.V3.Registry` | Registry grain interface (register, query, get). |
| `AgentRegistration` | record | `Core.V3.Registry` | Registration entry. Id 0-6. |
| `AgentQuery` | record | `Core.V3.Registry` | Query filter for registry lookups. Id 0-3. |
| `AgentRegistrationStartupTask` | class | `Core.V3.Registry` | Auto-discovers agent types at silo startup. |

## Tools

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `FileTools` | class | `Core.V3.Tools` | File read/write/list/search within workspace. |
| `ShellTools` | class | `Core.V3.Tools` | Shell + dotnet CLI execution with 120s timeout. |
| `WebTools` | class | `Core.V3.Tools` | HTTP fetch with SSRF protection + 50KB truncation. |
| `WorkspaceTools` | class | `Core.V3.Tools` | Get/set workspace path. |

## Internal Types (not part of public API)

| Type | Kind | Namespace | Purpose |
|------|------|-----------|---------|
| `DurableChatHistoryProvider` | internal sealed class | `Core.V3` | Bridges Orleans durable list to Microsoft.Agents.AI ChatHistoryProvider. |

## GrainType Assignments

| Grain | GrainType | Key Type |
|-------|-----------|----------|
| `Agent` | `"agent-v3"` | string |
| `DynamicAgent` | `"dynamic-agent-v3"` | string |
| `AgentRegistryGrain` | `"agent-registry"` | string |

## Naming Conventions

All types follow .NET naming conventions. No abbreviations except well-known ones (AI, P2P). All serializable records use positional constructor syntax with `[property: Id(n)]`. All `[Id(n)]` values are sequential starting at 0 with no gaps or duplicates.
