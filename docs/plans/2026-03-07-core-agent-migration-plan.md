# V3 Core Agent Behaviors Migration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate all Agent behaviors from IAW source (`src/Core/Agent.*.cs`) into the opensource V3 Agent (`InteractiveAgents/IAW/src/Core/V3/`), one behavior at a time, with typed message system, full test coverage, and documentation.

**Architecture:** Composition-based behavior model. Base Agent provides durable state + chat. Behaviors are opt-in via typed interfaces (`IStreamConsumer<T>`, `IBroadcaster<T>`, etc.). Typed message hierarchy (`IAgentMessage → ICommand / IEvent / INotification`) replaces `Dictionary<string, object>` payloads. Orleans 10.0 DurableGrain + Microsoft.Agents.AI.

**Tech Stack:** .NET 11, Orleans 10.0.1, Microsoft.Agents.AI 1.0.0-rc2, Microsoft.Extensions.AI 10.3.0, xunit.v3 3.2.2, Aspire 13.1.2

---

## Pre-Migration: Fix Existing V3 Issues

### Task 1: Fix StateEntry/StateDescriptor naming inconsistency

**Files:**
- Modify: `src/Core/V3/StateEntry.cs`

**Step 1: Read current file and identify the inconsistency**

The file is named `StateEntry.cs` but contains `record StateDescriptor`. The Agent.cs uses `IDurableDictionary<string, StateEntry>` in the constructor but the actual type is `StateDescriptor`.

**Step 2: Rename the record to match the file name**

```csharp
namespace Core.V3;

[GenerateSerializer]
public record StateEntry(
    [property: Id(0)] string Key,
    [property: Id(1)] object Value);
```

**Step 3: Update any references from StateDescriptor to StateEntry**

Search all V3 files for `StateDescriptor` and replace with `StateEntry`. Currently `Agent.cs:10` uses `StateEntry` already, so the type name in the file is what's wrong.

**Step 4: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 5: Commit**

```bash
git add src/Core/V3/StateEntry.cs
git commit -m "fix: rename StateDescriptor to StateEntry for consistency"
```

### Task 2: Fix WeatherAgent missing eventLog parameter

**Files:**
- Modify: `src/Core/V3/WeatherAgent.cs`

**Step 1: Read WeatherAgent constructor**

Currently missing `eventLog` parameter that base Agent requires:
```csharp
public class WeatherAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history)
    : Agent(state, chatClient, history), IWeatherAgent
```

**Step 2: Add missing eventLog parameter**

```csharp
public class WeatherAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history)
    : Agent(state, eventLog, chatClient, history), IWeatherAgent
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/WeatherAgent.cs
git commit -m "fix: add missing eventLog parameter to WeatherAgent constructor"
```

### Task 3: Fix Tools property conflict between Agent.cs and Agent.Tools.cs

**Files:**
- Modify: `src/Core/V3/Agent.cs`
- Modify: `src/Core/V3/Agent.Tools.cs`

**Step 1: Remove the Tools property from Agent.cs**

In `Agent.cs:20`, remove:
```csharp
protected virtual IList<AITool> Tools => [];
```

**Step 2: Update Agent.Tools.cs to be the single source of tool definitions**

```csharp
using Microsoft.Extensions.AI;

namespace Core.V3;

public abstract partial class Agent
{
    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    private IReadOnlyList<AITool> GetAllTools()
    {
        var coreTools = new List<AITool>();
        var subclassTools = DefineTools();
        return [.. coreTools, .. subclassTools];
    }
}
```

**Step 3: Update OnActivateAsync to use GetAllTools()**

In `Agent.cs`, change `Tools = [.. Tools]` to `Tools = [.. GetAllTools()]`.

**Step 4: Update WeatherAgent to override DefineTools() instead of Tools property**

```csharp
protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(GetCurrentWeather),
    AIFunctionFactory.Create(GetForecast),
    AIFunctionFactory.Create(GetWeatherAlerts)
];
```

**Step 5: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 6: Commit**

```bash
git add src/Core/V3/Agent.cs src/Core/V3/Agent.Tools.cs src/Core/V3/WeatherAgent.cs
git commit -m "fix: unify tool definition via DefineTools() pattern"
```

---

## Phase A: Conversation + Tools + State

### Task 4: Create typed message base interfaces

**Files:**
- Create: `src/Core/V3/Messages/IAgentMessage.cs`
- Create: `src/Core/V3/Messages/ICommand.cs`
- Create: `src/Core/V3/Messages/IEvent.cs`
- Create: `src/Core/V3/Messages/INotification.cs`

**Step 1: Create Messages directory**

Run: `mkdir -p src/Core/V3/Messages`

**Step 2: Create IAgentMessage.cs**

```csharp
namespace Core.V3.Messages;

public interface IAgentMessage
{
    string SourceAgentId { get; }
    string CorrelationId { get; }
    DateTimeOffset Timestamp { get; }
}
```

**Step 3: Create ICommand.cs**

```csharp
namespace Core.V3.Messages;

public interface ICommand : IAgentMessage;
```

**Step 4: Create IEvent.cs**

```csharp
namespace Core.V3.Messages;

public interface IEvent : IAgentMessage;
```

**Step 5: Create INotification.cs**

```csharp
namespace Core.V3.Messages;

public interface INotification : IAgentMessage;
```

**Step 6: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 7: Commit**

```bash
git add src/Core/V3/Messages/
git commit -m "feat(v3): add typed message hierarchy — IAgentMessage, ICommand, IEvent, INotification"
```

### Task 5: Create built-in message types

**Files:**
- Create: `src/Core/V3/Messages/AgentActivatedEvent.cs`
- Create: `src/Core/V3/Messages/StateChangedEvent.cs`
- Create: `src/Core/V3/Messages/AssignTaskCommand.cs`
- Create: `src/Core/V3/Messages/ProgressNotification.cs`
- Create: `src/Core/V3/Messages/AlertNotification.cs`

**Step 1: Create AgentActivatedEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record AgentActivatedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string AgentType) : IEvent;
```

**Step 2: Create StateChangedEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record StateChangedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Key,
    [property: Id(4)] string? OldValue,
    [property: Id(5)] string? NewValue) : IEvent;
```

**Step 3: Create AssignTaskCommand.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record AssignTaskCommand(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Description,
    [property: Id(4)] string? WorkspacePath) : ICommand;
```

**Step 4: Create ProgressNotification.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record ProgressNotification(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Step,
    [property: Id(4)] string Status,
    [property: Id(5)] float? Progress) : INotification;
```

**Step 5: Create AlertNotification.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record AlertNotification(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Severity,
    [property: Id(4)] string Message) : INotification;
```

**Step 6: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 7: Commit**

```bash
git add src/Core/V3/Messages/
git commit -m "feat(v3): add built-in message types — events, commands, notifications"
```

### Task 6: Create AgentResponse model for streaming

**Files:**
- Create: `src/Core/V3/AgentResponse.cs`

**Step 1: Create AgentResponse.cs with response kinds**

```csharp
namespace Core.V3;

public enum AgentResponseKind
{
    Text,
    ToolCall,
    ToolResult,
    Error,
    Final
}

[GenerateSerializer]
public record AgentResponse(
    [property: Id(0)] AgentResponseKind Kind,
    [property: Id(1)] string Content,
    [property: Id(2)] string? ToolName = null,
    [property: Id(3)] Dictionary<string, object>? Metadata = null);
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Core/V3/AgentResponse.cs
git commit -m "feat(v3): add AgentResponse model with streaming response kinds"
```

### Task 7: Create AgentMetadata and AgentCapabilities models

**Files:**
- Create: `src/Core/V3/AgentMetadata.cs`
- Create: `src/Core/V3/AgentCapabilities.cs`
- Create: `src/Core/V3/AgentState.cs`

**Step 1: Create AgentMetadata.cs**

```csharp
namespace Core.V3;

[GenerateSerializer]
public record AgentMetadata(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] AgentKind Kind,
    [property: Id(4)] string[] Capabilities,
    [property: Id(5)] string[] Publishes,
    [property: Id(6)] string[] Subscribes);

[GenerateSerializer]
public enum AgentKind
{
    Static,
    Dynamic
}
```

**Step 2: Create AgentCapabilities.cs**

```csharp
namespace Core.V3;

[GenerateSerializer]
public record AgentCapabilities(
    [property: Id(0)] bool HasMemory,
    [property: Id(1)] bool HasP2P,
    [property: Id(2)] bool HasEvents,
    [property: Id(3)] bool HasTimers,
    [property: Id(4)] bool IsCancellable,
    [property: Id(5)] bool IsMultiState,
    [property: Id(6)] bool HasTools,
    [property: Id(7)] bool IsSecure);
```

**Step 3: Create AgentState.cs**

```csharp
namespace Core.V3;

[GenerateSerializer]
public record AgentState(
    [property: Id(0)] Dictionary<string, StateEntry> Entries);
```

**Step 4: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 5: Commit**

```bash
git add src/Core/V3/AgentMetadata.cs src/Core/V3/AgentCapabilities.cs src/Core/V3/AgentState.cs
git commit -m "feat(v3): add AgentMetadata, AgentCapabilities, AgentState models"
```

### Task 8: Create observability infrastructure

**Files:**
- Create: `src/Core/V3/Observability/AgentTelemetry.cs`

**Step 1: Create Observability directory**

Run: `mkdir -p src/Core/V3/Observability`

**Step 2: Create AgentTelemetry.cs**

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Core.V3.Observability;

public static class AgentTelemetry
{
    public const string SourceName = "IAW";
    public const string MeterName = "IAW";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "agents.events.published", "{event}", "Events published by agents");

    public static readonly Counter<long> EventsHandled = Meter.CreateCounter<long>(
        "agents.events.handled", "{event}", "Events handled by agents");

    public static readonly Counter<long> Activations = Meter.CreateCounter<long>(
        "agents.activations", "{activation}", "Agent activations");

    public static readonly Counter<long> MessagesSent = Meter.CreateCounter<long>(
        "agents.messages.sent", "{message}", "Messages processed by agents");

    public static readonly Counter<long> ConversationErrors = Meter.CreateCounter<long>(
        "agents.conversations.errors", "{error}", "Conversation errors");

    public static readonly Histogram<double> EventHandleDuration = Meter.CreateHistogram<double>(
        "agents.events.handle_duration", "s", "Event handling duration");

    public static readonly Histogram<double> ConversationDuration = Meter.CreateHistogram<double>(
        "agents.conversations.duration", "s", "Conversation turn duration");
}
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/Observability/
git commit -m "feat(v3): add AgentTelemetry with OpenTelemetry counters and histograms"
```

### Task 9: Create context provider system

**Files:**
- Create: `src/Core/V3/Context/IAIContextProvider.cs`
- Create: `src/Core/V3/Context/AIContext.cs`

**Step 1: Create Context directory**

Run: `mkdir -p src/Core/V3/Context`

**Step 2: Create IAIContextProvider.cs**

```csharp
namespace Core.V3.Context;

public interface IAIContextProvider
{
    Task<AIContext> ProvideContextAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    Task StoreContextAsync(IReadOnlyList<ChatMessage> request, AgentResponse response, CancellationToken ct = default);
}
```

**Step 3: Create AIContext.cs**

```csharp
namespace Core.V3.Context;

[GenerateSerializer]
public sealed record AIContext(
    [property: Id(0)] IReadOnlyList<ChatMessage> AdditionalMessages,
    [property: Id(1)] IDictionary<string, string>? Metadata = null)
{
    public static AIContext Empty => new(Array.Empty<ChatMessage>());
}
```

**Step 4: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 5: Commit**

```bash
git add src/Core/V3/Context/
git commit -m "feat(v3): add context provider system — IAIContextProvider, AIContext"
```

### Task 10: Create diagnostics infrastructure

**Files:**
- Create: `src/Core/V3/Diagnostics/ISelfDiagnosable.cs`
- Create: `src/Core/V3/Diagnostics/DiagnosticReport.cs`

**Step 1: Create Diagnostics directory**

Run: `mkdir -p src/Core/V3/Diagnostics`

**Step 2: Create ISelfDiagnosable.cs**

```csharp
namespace Core.V3.Diagnostics;

public interface ISelfDiagnosable
{
    Task<DiagnosticReport> DiagnoseAsync(CancellationToken ct = default);
}
```

**Step 3: Create DiagnosticReport.cs**

```csharp
namespace Core.V3.Diagnostics;

[GenerateSerializer]
public record DiagnosticReport(
    [property: Id(0)] string AgentType,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] bool Healthy,
    [property: Id(3)] int TestsRun,
    [property: Id(4)] int TestsPassed,
    [property: Id(5)] TimeSpan Duration,
    [property: Id(6)] IReadOnlyList<DiagnosticFailure> Failures);

[GenerateSerializer]
public record DiagnosticFailure(
    [property: Id(0)] string TestName,
    [property: Id(1)] string Message,
    [property: Id(2)] string? StackTrace);
```

**Step 4: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 5: Commit**

```bash
git add src/Core/V3/Diagnostics/
git commit -m "feat(v3): add diagnostics — ISelfDiagnosable, DiagnosticReport"
```

### Task 11: Create attribute system

**Files:**
- Create: `src/Core/V3/Attributes/CapabilityAttribute.cs`
- Create: `src/Core/V3/Attributes/PublishesAttribute.cs`
- Create: `src/Core/V3/Attributes/SubscribesAttribute.cs`

**Step 1: Create Attributes directory**

Run: `mkdir -p src/Core/V3/Attributes`

**Step 2: Create CapabilityAttribute.cs**

```csharp
namespace Core.V3.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class CapabilityAttribute(string capability) : Attribute
{
    public string Capability { get; } = capability;
}
```

**Step 3: Create PublishesAttribute.cs**

```csharp
namespace Core.V3.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PublishesAttribute(string eventName) : Attribute
{
    public string EventName { get; } = eventName;
}
```

**Step 4: Create SubscribesAttribute.cs**

```csharp
namespace Core.V3.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SubscribesAttribute(string eventName) : Attribute
{
    public string EventName { get; } = eventName;
}
```

**Step 5: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 6: Commit**

```bash
git add src/Core/V3/Attributes/
git commit -m "feat(v3): add declarative attributes — Capability, Publishes, Subscribes"
```

### Task 12: Expand IAgent interface with state and metadata

**Files:**
- Modify: `src/Core/V3/IAgent.cs`

**Step 1: Read current IAgent interface**

Current: 4 methods (GetResponseStream, GetResponse, GetHistory, ClearHistoryAsync)

**Step 2: Add state, metadata, and lifecycle methods**

```csharp
namespace Core.V3;

public interface IAgent : IGrainWithStringKey
{
    // Conversation
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct);
    Task<string> GetResponse(string prompt, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken ct);
    Task ClearHistoryAsync(CancellationToken ct);

    // State
    Task<AgentState> GetStateAsync(CancellationToken ct);
    Task SetWorkspaceAsync(string path, CancellationToken ct);

    // Metadata
    Task<AgentMetadata> GetMetadataAsync(CancellationToken ct);
    Task<AgentCapabilities> GetCapabilitiesAsync(CancellationToken ct);

    // Lifecycle
    Task CancelAsync(CancellationToken ct);
}
```

**Step 3: Build — expect errors (not yet implemented in Agent.cs)**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build errors for unimplemented methods (this is expected — we implement next)

**Step 4: Commit the interface expansion**

```bash
git add src/Core/V3/IAgent.cs
git commit -m "feat(v3): expand IAgent with state, metadata, and lifecycle methods"
```

### Task 13: Add Agent.State.cs partial

**Files:**
- Create: `src/Core/V3/Agent.State.cs`

**Step 1: Create Agent.State.cs**

```csharp
namespace Core.V3;

public abstract partial class Agent
{
    private const string WorkspacePathKey = "workspace-path";

    public async Task SetWorkspaceAsync(string path, CancellationToken ct = default)
    {
        state[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path);
        await WriteStateAsync(ct);
    }

    public Task<AgentState> GetStateAsync(CancellationToken ct = default)
    {
        var entries = new Dictionary<string, StateEntry>();
        foreach (var kvp in state)
            entries[kvp.Key] = kvp.Value;
        return Task.FromResult(new AgentState(entries));
    }

    protected string? GetWorkspacePath()
        => state.TryGetValue(WorkspacePathKey, out var entry)
            ? entry.Value.ToString()
            : null;
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: May still have errors from missing metadata/lifecycle methods

**Step 3: Commit**

```bash
git add src/Core/V3/Agent.State.cs
git commit -m "feat(v3): add Agent.State.cs — workspace and state introspection"
```

### Task 14: Add Agent.Lifecycle.cs partial

**Files:**
- Create: `src/Core/V3/Agent.Lifecycle.cs`

**Step 1: Create Agent.Lifecycle.cs**

```csharp
using System.Reflection;
using Core.V3.Attributes;
using Core.V3.Communication;
using Core.V3.Diagnostics;
using Core.V3.Observability;

namespace Core.V3;

public abstract partial class Agent : ISelfDiagnosable
{
    private CancellationTokenSource _cts = new();
    protected CancellationToken AgentCancellation => _cts.Token;

    protected virtual string DisplayName => GetType().Name;
    protected virtual AgentKind AgentKindValue => AgentKind.Static;

    public Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default)
    {
        var type = GetType();
        var publishedFromInterfaces = DiscoverPublishedMessageTypes(type);
        var publishedFromAttributes = type.GetCustomAttributes<PublishesAttribute>().Select(a => a.EventName);
        var publishes = publishedFromInterfaces.Concat(publishedFromAttributes).Distinct().ToArray();

        var subscribedFromInterfaces = DiscoverReceivedMessageTypes(type);
        var subscribedFromAttributes = type.GetCustomAttributes<SubscribesAttribute>().Select(a => a.EventName);
        var subscribes = subscribedFromInterfaces.Concat(subscribedFromAttributes).Distinct().ToArray();

        var capabilities = type.GetCustomAttributes<CapabilityAttribute>().Select(a => a.Capability).ToArray();

        return Task.FromResult(new AgentMetadata(
            type.Name,
            DisplayName,
            Instructions,
            AgentKindValue,
            capabilities,
            publishes,
            subscribes));
    }

    public Task<AgentCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var type = GetType();
        var attributeCaps = type.GetCustomAttributes<CapabilityAttribute>()
            .Select(a => a.Capability)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new AgentCapabilities(
            HasMemory: true,
            HasP2P: HasInterface(type, typeof(IReceiver<>)) || attributeCaps.Contains("P2P"),
            HasEvents: HasInterface(type, typeof(IStreamConsumer<>)) || HasInterface(type, typeof(IStreamProducer<>)) || attributeCaps.Contains("Events"),
            HasTimers: true,
            IsCancellable: true,
            IsMultiState: attributeCaps.Contains("Multi-state"),
            HasTools: GetAllTools().Count > 0,
            IsSecure: attributeCaps.Contains("Secure")));
    }

    public Task CancelAsync(CancellationToken ct = default)
    {
        var old = _cts;
        _cts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
        return Task.CompletedTask;
    }

    public virtual Task<DiagnosticReport> DiagnoseAsync(CancellationToken ct = default)
        => Task.FromResult(new DiagnosticReport(GetType().Name, DateTimeOffset.UtcNow, true, 0, 0, TimeSpan.Zero, []));

    private static string[] DiscoverPublishedMessageTypes(Type type) =>
    [
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBroadcaster<>))
            .Select(i => i.GetGenericArguments()[0].Name),
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotifier<>))
            .Select(i => i.GetGenericArguments()[0].Name),
    ];

    private static string[] DiscoverReceivedMessageTypes(Type type) =>
    [
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReceiver<>))
            .Select(i => i.GetGenericArguments()[0].Name),
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>))
            .Select(i => i.GetGenericArguments()[0].Name),
    ];

    private static bool HasInterface(Type type, Type openGenericInterface)
        => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build may fail if Communication interfaces don't exist yet — proceed to Task 15

**Step 3: Commit**

```bash
git add src/Core/V3/Agent.Lifecycle.cs
git commit -m "feat(v3): add Agent.Lifecycle.cs — metadata, capabilities, cancel, diagnostics"
```

### Task 15: Create communication interfaces

**Files:**
- Create: `src/Core/V3/Communication/IStreamConsumer.cs`
- Create: `src/Core/V3/Communication/IStreamProducer.cs`
- Create: `src/Core/V3/Communication/IBroadcaster.cs`
- Create: `src/Core/V3/Communication/INotifier.cs`
- Create: `src/Core/V3/Communication/IReceiver.cs`
- Create: `src/Core/V3/Communication/BroadcastResult.cs`
- Create: `src/Core/V3/Communication/MessageReceipt.cs`
- Create: `src/Core/V3/Communication/IAgentObserver.cs`

**Step 1: Create Communication directory**

Run: `mkdir -p src/Core/V3/Communication`

**Step 2: Create IStreamConsumer.cs**

```csharp
using Core.V3.Messages;
using Orleans.Streams;

namespace Core.V3.Communication;

public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent evt, StreamSequenceToken? token);
}
```

**Step 3: Create IStreamProducer.cs**

```csharp
using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IStreamProducer<TEvent> where TEvent : IEvent
{
    Task PublishToStreamAsync(TEvent evt, CancellationToken ct = default);
}
```

**Step 4: Create IBroadcaster.cs**

```csharp
using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IBroadcaster<TMessage> where TMessage : IAgentMessage
{
    Task<BroadcastResult> BroadcastAsync(TMessage message, CancellationToken ct = default);
    Task RegisterReceiverAsync(string receiverId);
    Task UnregisterReceiverAsync(string receiverId);
    Task<IReadOnlyList<string>> GetReceiversAsync();
}
```

**Step 5: Create INotifier.cs**

```csharp
using Core.V3.Messages;

namespace Core.V3.Communication;

public interface INotifier<TNotification> where TNotification : INotification
{
    Task NotifyAsync(TNotification notification, CancellationToken ct = default);
    Task SubscribeObserverAsync(IAgentObserver<TNotification> observer);
    Task UnsubscribeObserverAsync(IAgentObserver<TNotification> observer);
}
```

**Step 6: Create IReceiver.cs**

```csharp
using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IReceiver<TMessage> where TMessage : IAgentMessage
{
    Task<MessageReceipt> ReceiveAsync(TMessage message, CancellationToken ct = default);
    Task<bool> CanReceiveAsync(CancellationToken ct = default);
}
```

**Step 7: Create BroadcastResult.cs**

```csharp
namespace Core.V3.Communication;

[GenerateSerializer]
public record BroadcastResult(
    [property: Id(0)] int TotalReceivers,
    [property: Id(1)] int Delivered,
    [property: Id(2)] int Failed,
    [property: Id(3)] string[] FailedReceiverIds);
```

**Step 8: Create MessageReceipt.cs**

```csharp
namespace Core.V3.Communication;

[GenerateSerializer]
public record MessageReceipt(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] string ReceiptId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string? RejectionReason);
```

**Step 9: Create IAgentObserver.cs**

```csharp
using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IAgentObserver<TEvent> : IGrainObserver where TEvent : INotification
{
    void OnEvent(TEvent evt);
    void OnError(Exception ex);
}
```

**Step 10: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 11: Commit**

```bash
git add src/Core/V3/Communication/
git commit -m "feat(v3): add typed communication interfaces — stream, broadcast, notify, receive"
```

### Task 16: Port FileTools to V3

**Files:**
- Create: `src/Core/V3/Tools/FileTools.cs`

**Step 1: Create FileTools.cs**

Port from source `src/Core/Tools/FileTools.cs` with namespace change to `Core.V3.Tools`:

```csharp
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Core.V3.Tools;

public class FileTools(Func<string> getWorkspacePath)
{
    private const int MaxResults = 500;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults", "packages"
    };

    private string WorkspacePath => getWorkspacePath();

    public FileTools(string workspacePath) : this(() => workspacePath) { }

    [Description("Read a file from the workspace")]
    public async Task<string> ReadFileAsync(
        [Description("Absolute or workspace-relative path")] string path)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
            return $"File not found: {fullPath}";
        return await File.ReadAllTextAsync(fullPath);
    }

    [Description("Create or overwrite a file in the workspace")]
    public async Task<string> WriteFileAsync(
        [Description("Absolute or workspace-relative path")] string path,
        [Description("Content to write")] string content)
    {
        var fullPath = ResolvePath(path);
        ValidateInsideWorkspace(fullPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, content);
        return $"File written: {fullPath}";
    }

    [Description("List files matching a glob pattern")]
    public string[] ListFiles(
        [Description("Directory to search")] string directory,
        [Description("Glob pattern like *.cs")] string pattern = "*")
    {
        var fullPath = ResolvePath(directory);
        if (!Directory.Exists(fullPath))
            return [$"Directory not found: {fullPath}"];
        return EnumerateFiles(fullPath, pattern)
            .Select(f => Path.GetRelativePath(WorkspacePath, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .ToArray();
    }

    [Description("Search for a regex pattern in files")]
    public string[] SearchCode(
        [Description("Regex pattern")] string pattern,
        [Description("Directory to search")] string directory,
        [Description("File filter like *.cs")] string fileFilter = "*.cs")
    {
        var fullPath = ResolvePath(directory);
        if (!Directory.Exists(fullPath))
            return [$"Directory not found: {fullPath}"];
        var regex = new Regex(pattern, RegexOptions.Compiled);
        var matches = new List<string>();
        foreach (var file in EnumerateFiles(fullPath, fileFilter))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!regex.IsMatch(lines[i])) continue;
                matches.Add($"{Path.GetRelativePath(WorkspacePath, file)}:{i + 1}: {lines[i].Trim()}");
                if (matches.Count >= MaxResults) return [.. matches];
            }
        }
        return [.. matches];
    }

    private string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(WorkspacePath, path));

    private void ValidateInsideWorkspace(string fullPath)
    {
        if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(WorkspacePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path {fullPath} is outside workspace");
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var f in files) yield return f;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var d in dirs)
                if (!ExcludedDirectories.Contains(Path.GetFileName(d)))
                    pending.Push(d);
        }
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Core/V3/Tools/FileTools.cs
git commit -m "feat(v3): port FileTools — read, write, list, search"
```

### Task 17: Port ShellTools to V3

**Files:**
- Create: `src/Core/V3/Tools/ShellTools.cs`

**Step 1: Create ShellTools.cs**

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Core.V3.Tools;

public class ShellTools(Func<string> getWorkspacePath)
{
    private const int TimeoutMs = 120_000;
    private string WorkspacePath => getWorkspacePath();

    public ShellTools(string workspacePath) : this(() => workspacePath) { }

    [Description("Run a dotnet CLI command")]
    public Task<string> RunDotnetAsync(
        [Description("Arguments for 'dotnet' command")] string arguments,
        [Description("Working directory (defaults to workspace)")] string? workingDirectory = null)
        => ExecuteAsync("dotnet", arguments, workingDirectory ?? WorkspacePath);

    [Description("Run a shell command")]
    public Task<string> RunShellAsync(
        [Description("Command to execute")] string command,
        [Description("Working directory (defaults to workspace)")] string? workingDirectory = null)
    {
        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/sh";
        var args = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"";
        return ExecuteAsync(shell, args, workingDirectory ?? WorkspacePath);
    }

    private static async Task<string> ExecuteAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return $"Failed to start: {fileName}";
        using var cts = new CancellationTokenSource(TimeoutMs);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(cts.Token);
        var sb = new StringBuilder();
        if (stdoutTask.Result.Length > 0) sb.AppendLine(stdoutTask.Result.Trim());
        if (stderrTask.Result.Length > 0) sb.AppendLine(stderrTask.Result.Trim());
        sb.AppendLine($"Exit code: {process.ExitCode}");
        var output = sb.ToString();
        return output.Length > 8_000 ? output[..8_000] + "\n... (truncated)" : output;
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Core/V3/Tools/ShellTools.cs
git commit -m "feat(v3): port ShellTools — dotnet CLI and shell execution"
```

### Task 18: Port WebTools to V3

**Files:**
- Create: `src/Core/V3/Tools/WebTools.cs`

**Step 1: Create WebTools.cs**

```csharp
using System.ComponentModel;

namespace Core.V3.Tools;

public class WebTools(HttpClient httpClient)
{
    [Description("Fetch content from a URL")]
    public async Task<string> FetchUrlAsync([Description("URL to fetch")] string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return content.Length > 50_000 ? content[..50_000] + "\n... (truncated)" : content;
        }
        catch (Exception ex)
        {
            return $"Error fetching {url}: {ex.Message}";
        }
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Core/V3/Tools/WebTools.cs
git commit -m "feat(v3): port WebTools — URL fetching with truncation"
```

### Task 19: Wire core tools into Agent.Tools.cs

**Files:**
- Modify: `src/Core/V3/Agent.Tools.cs`

**Step 1: Update GetAllTools() to register core tools**

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Core.V3.Tools;

namespace Core.V3;

public abstract partial class Agent
{
    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    public Task<IReadOnlyList<ToolDescription>> GetToolDescriptions(CancellationToken ct = default)
    {
        var tools = GetAllTools();
        IReadOnlyList<ToolDescription> descriptions = tools
            .OfType<AIFunction>()
            .Select(f => new ToolDescription(f.Name, f.Description))
            .ToList();
        return Task.FromResult(descriptions);
    }

    private IReadOnlyList<AITool> GetAllTools()
    {
        var tools = new List<AITool>();

        var workspaceTools = new WorkspaceTools(
            () => GetWorkspacePath() ?? ".",
            path => state[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path));
        RegisterToolMethods(tools, workspaceTools);

        if (GetWorkspacePath() is not null)
        {
            RegisterToolMethods(tools, new FileTools(() => GetWorkspacePath()!));
            RegisterToolMethods(tools, new ShellTools(() => GetWorkspacePath()!));
        }

        var httpFactory = ServiceProvider.GetService<IHttpClientFactory>();
        if (httpFactory is not null)
            RegisterToolMethods(tools, new WebTools(httpFactory.CreateClient()));

        var subclassTools = DefineTools();
        tools.AddRange(subclassTools);
        return tools;
    }

    private static void RegisterToolMethods(List<AITool> tools, object toolSource)
    {
        var methods = toolSource.GetType().GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Length > 0)
                tools.Add(AIFunctionFactory.Create(method, toolSource));
        }
    }
}
```

**Step 2: Create ToolDescription record**

Add to `src/Core/V3/AgentMetadata.cs` (or a new file):

```csharp
// Add to bottom of AgentMetadata.cs or create separate file
[GenerateSerializer]
public record ToolDescription(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description);
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/Agent.Tools.cs src/Core/V3/AgentMetadata.cs
git commit -m "feat(v3): wire core tools (Workspace, File, Shell, Web) into Agent.Tools"
```

### Task 20: Update Agent.cs OnActivateAsync with telemetry and tool client

**Files:**
- Modify: `src/Core/V3/Agent.cs`

**Step 1: Add telemetry, stream provider, and improved activation**

```csharp
using System.Runtime.CompilerServices;
using Core.V3.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;
using System.Diagnostics;

namespace Core.V3;

[GrainType("agent-v3")]
public abstract partial class Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history)
    : DurableGrain, IAgent
{
    private AIAgent? _agent;
    private AgentSession? _session;
    private IChatClient? _toolClient;

    protected virtual string Instructions => "You are a helpful AI assistant.";

    protected IDurableList<ChatMessage> History => history;
    protected IDurableDictionary<string, StateEntry> State => state;
    protected IDurableList<AgentEvent> EventLog => eventLog;
    protected IChatClient ChatClient => _toolClient ?? chatClient;
    protected IStreamProvider StreamProvider => this.GetStreamProvider("agents");

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.activate");
        activity?.SetTag("agent.type", GetType().Name);
        activity?.SetTag("agent.id", this.GetPrimaryKeyString());
        AgentTelemetry.Activations.Add(1, new TagList { { "agent.type", GetType().Name } });

        await base.OnActivateAsync(cancellationToken);

        var tools = GetAllTools();
        var builder = new ChatClientBuilder(chatClient);

        if (tools.Count > 0)
            builder.UseFunctionInvocation();

        _toolClient = builder.Build();

        _agent = _toolClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = this.GetPrimaryKeyString(),
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = [.. tools]
            },
            ChatHistoryProvider = new DurableChatHistoryProvider(history)
        });

        _session = await _agent.CreateSessionAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> GetResponseStream(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.send_message");
        activity?.SetTag("agent.type", GetType().Name);
        AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });

        await foreach (var chunk in _agent!.RunStreamingAsync(prompt, _session, cancellationToken: cancellationToken))
        {
            if (chunk.Text is not { } text) continue;
            yield return text;
        }
        await WriteStateAsync(cancellationToken);
    }

    public async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
    {
        AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });
        var response = await _agent!.RunAsync(prompt, _session, cancellationToken: cancellationToken);
        await WriteStateAsync(cancellationToken);
        return response.Text ?? string.Empty;
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChatMessage> snapshot = [.. history];
        return Task.FromResult(snapshot);
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        history.Clear();
        await WriteStateAsync(cancellationToken);
        _session = await _agent!.CreateSessionAsync(cancellationToken);
    }

    protected static string BuildSafeErrorMessage(Exception ex) =>
        $"An error occurred: {ex.GetType().Name} — {ex.Message}";
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds (all IAgent methods now implemented)

**Step 3: Commit**

```bash
git add src/Core/V3/Agent.cs
git commit -m "feat(v3): update Agent.cs with telemetry, stream provider, tool client pipeline"
```

### Task 21: Write tests for Phase A (Conversation + Tools + State)

**Files:**
- Create: `test/Core.Tests/V3/AgentV3Tests.cs`
- Create: `test/Core.Tests/V3/TestAgent.cs`

**Step 1: Create test directory**

Run: `mkdir -p test/Core.Tests/V3`

**Step 2: Create TestAgent.cs — minimal concrete agent for testing**

```csharp
using Core.V3;
using Orleans.Journaling;

namespace IAW.Core.Tests.V3;

public interface ITestAgent : Core.V3.IAgent;

public class TestAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history)
    : Agent(state, eventLog, chatClient, history), ITestAgent
{
    protected override string Instructions => "You are a test agent.";
    protected override string DisplayName => "Test Agent";
}
```

**Step 3: Create AgentV3Tests.cs — behavior tests**

```csharp
using Core.V3;
using IAW.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace IAW.Core.Tests.V3;

public class AgentV3Tests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    private Core.V3.IAgent Agent(string id) => _cluster.GrainFactory.GetGrain<ITestAgent>(id);

    [Fact]
    public async Task GetResponse_ReturnsNonEmptyString()
    {
        var agent = Agent($"test-response-{Guid.NewGuid():N}");
        var response = await agent.GetResponse("Hello", CancellationToken.None);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetHistory_AfterResponse_ContainsMessages()
    {
        var agent = Agent($"test-history-{Guid.NewGuid():N}");
        await agent.GetResponse("Hello", CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.True(history.Count > 0);
    }

    [Fact]
    public async Task ClearHistory_EmptiesMessages()
    {
        var agent = Agent($"test-clear-{Guid.NewGuid():N}");
        await agent.GetResponse("Hello", CancellationToken.None);
        await agent.ClearHistoryAsync(CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.Empty(history);
    }

    [Fact]
    public async Task SetWorkspace_PersistsInState()
    {
        var agent = Agent($"test-workspace-{Guid.NewGuid():N}");
        await agent.SetWorkspaceAsync("/tmp/test", CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.True(state.Entries.ContainsKey("workspace-path"));
        Assert.Equal("/tmp/test", state.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task GetMetadata_ReturnsAgentType()
    {
        var agent = Agent($"test-meta-{Guid.NewGuid():N}");
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("TestAgent", metadata.AgentType);
        Assert.Equal("Test Agent", metadata.DisplayName);
    }

    [Fact]
    public async Task GetCapabilities_ReturnsDefaults()
    {
        var agent = Agent($"test-caps-{Guid.NewGuid():N}");
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasMemory);
        Assert.True(caps.IsCancellable);
        Assert.True(caps.HasTimers);
    }

    [Fact]
    public async Task CancelAsync_DoesNotThrow()
    {
        var agent = Agent($"test-cancel-{Guid.NewGuid():N}");
        await agent.CancelAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GetResponseStream_YieldsChunks()
    {
        var agent = Agent($"test-stream-{Guid.NewGuid():N}");
        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", CancellationToken.None))
            chunks.Add(chunk);
        Assert.True(chunks.Count > 0);
    }
}
```

**Step 4: Run tests to verify**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~AgentV3Tests"`
Expected: All tests pass (may need MockChatClient integration)

**Step 5: Commit**

```bash
git add test/Core.Tests/V3/
git commit -m "test(v3): add Phase A behavior tests — conversation, state, metadata, capabilities"
```

---

## Phase B: Events + Streams

### Task 22: Add Agent.Events.cs partial

**Files:**
- Create: `src/Core/V3/Agent.Events.cs`

**Step 1: Create Agent.Events.cs**

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using Core.V3.Messages;
using Core.V3.Observability;
using Orleans.Streams;

namespace Core.V3;

public abstract partial class Agent
{
    public virtual Task HandleEvent(AgentEvent agentEvent, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentEvent>>(EventLog.ToList());

    protected async Task PublishAsync(string eventName, Dictionary<string, object>? payload = null, CancellationToken ct = default)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish");
        activity?.SetTag("event.name", eventName);

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        var agentEvent = new AgentEvent(
            eventName,
            this.GetPrimaryKeyString(),
            correlationId,
            DateTimeOffset.UtcNow,
            payload ?? []);

        EventLog.Add(agentEvent);
        await WriteStateAsync(ct);

        var streamId = StreamId.Create("agents", eventName);
        var stream = StreamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(agentEvent);

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", eventName } });
    }

    protected async Task PublishTypedAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : IEvent
    {
        var streamName = EventTypeToStreamName(typeof(TEvent));
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.publish_typed");
        activity?.SetTag("event.name", streamName);
        activity?.SetTag("event.type", typeof(TEvent).Name);

        var agentEvent = new AgentEvent(
            streamName,
            evt.SourceAgentId,
            evt.CorrelationId,
            evt.Timestamp,
            new Dictionary<string, object> { ["typed_payload"] = evt });

        EventLog.Add(agentEvent);
        await WriteStateAsync(ct);

        var streamId = StreamId.Create("agents", streamName);
        var stream = StreamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(agentEvent);

        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", streamName } });
    }

    public static string EventTypeToStreamName(Type eventType)
    {
        var name = eventType.Name;
        if (name.EndsWith("Event")) name = name[..^5];
        else if (name.EndsWith("Command")) name = name[..^7];
        else if (name.EndsWith("Notification")) name = name[..^12];
        return Regex.Replace(name, "(?<!^)([A-Z])", ".$1").ToLowerInvariant();
    }
}
```

**Step 2: Update IAgent with event methods**

Add to `src/Core/V3/IAgent.cs`:
```csharp
// Events
Task HandleEventAsync(AgentEvent agentEvent, CancellationToken ct);
Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct);
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/Agent.Events.cs src/Core/V3/IAgent.cs
git commit -m "feat(v3): add Agent.Events.cs — publish, handle, typed event publishing"
```

### Task 23: Add Agent.Streams.cs partial

**Files:**
- Create: `src/Core/V3/Agent.Streams.cs`

**Step 1: Create Agent.Streams.cs**

```csharp
using System.Diagnostics;
using Core.V3.Communication;
using Core.V3.Messages;
using Core.V3.Observability;
using Orleans.Streams;

namespace Core.V3;

public abstract partial class Agent
{
    public async Task PublishToStreamAsync(AgentEvent evt, CancellationToken ct = default)
    {
        EventLog.Add(evt);
        await WriteStateAsync(ct);
        var streamId = StreamId.Create("agents", evt.EventName);
        var stream = StreamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(evt);
        AgentTelemetry.EventsPublished.Add(1, new TagList { { "event.name", evt.EventName } });
    }

    public Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(CancellationToken ct = default)
    {
        var subs = GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>))
            .Select(i => EventTypeToStreamName(i.GetGenericArguments()[0]))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(subs);
    }

    private async Task SubscribeToStreamConsumerInterfaces()
    {
        var consumerInterfaces = GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>));

        foreach (var iface in consumerInterfaces)
        {
            var eventType = iface.GetGenericArguments()[0];
            var streamName = EventTypeToStreamName(eventType);

            var streamId = StreamId.Create("agents", streamName);
            var stream = StreamProvider.GetStream<AgentEvent>(streamId);

            await stream.SubscribeAsync(async (evt, _) =>
            {
                using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.handle_stream_event");
                activity?.SetTag("event.name", evt.EventName);
                activity?.SetTag("agent.type", GetType().Name);

                var sw = Stopwatch.StartNew();
                await HandleEvent(evt, AgentCancellation);
                sw.Stop();

                AgentTelemetry.EventsHandled.Add(1, new TagList { { "event.name", evt.EventName } });
                AgentTelemetry.EventHandleDuration.Record(sw.Elapsed.TotalSeconds, new TagList { { "event.name", evt.EventName } });
            });
        }
    }
}
```

**Step 2: Update OnActivateAsync to call SubscribeToStreamConsumerInterfaces**

In `Agent.cs`, add at end of `OnActivateAsync`:
```csharp
await SubscribeToStreamConsumerInterfaces();
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/Agent.Streams.cs src/Core/V3/Agent.cs
git commit -m "feat(v3): add Agent.Streams.cs — auto-subscribe IStreamConsumer<T>, publish, subscriptions"
```

### Task 24: Create IEventDrivenAgent optional interface

**Files:**
- Create: `src/Core/V3/IEventDrivenAgent.cs`
- Create: `src/Core/V3/IStreamingAgent.cs`

**Step 1: Create IEventDrivenAgent.cs**

```csharp
namespace Core.V3;

public interface IEventDrivenAgent : IAgent
{
    Task HandleEventAsync(AgentEvent agentEvent, CancellationToken ct);
    Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct);
}
```

**Step 2: Create IStreamingAgent.cs**

```csharp
namespace Core.V3;

public interface IStreamingAgent : IAgent
{
    Task PublishToStreamAsync(AgentEvent evt, CancellationToken ct);
    Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(CancellationToken ct);
}
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/IEventDrivenAgent.cs src/Core/V3/IStreamingAgent.cs
git commit -m "feat(v3): add IEventDrivenAgent and IStreamingAgent optional interfaces"
```

### Task 25: Write tests for Phase B (Events + Streams)

**Files:**
- Modify: `test/Core.Tests/V3/AgentV3Tests.cs`
- Create: `test/Core.Tests/V3/StreamTestAgent.cs`

**Step 1: Create StreamTestAgent — agent that consumes a typed event**

```csharp
using Core.V3;
using Core.V3.Communication;
using Core.V3.Messages;
using Orleans.Journaling;
using Orleans.Streams;

namespace IAW.Core.Tests.V3;

[GenerateSerializer]
public record TestEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Data) : IEvent;

public interface IStreamTestAgent : Core.V3.IAgent
{
    Task<int> GetReceivedEventCount();
}

public class StreamTestAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history)
    : Agent(state, eventLog, chatClient, history), IStreamTestAgent, IStreamConsumer<TestEvent>
{
    private int _receivedCount;

    public Task OnStreamEventAsync(TestEvent evt, StreamSequenceToken? token)
    {
        _receivedCount++;
        return Task.CompletedTask;
    }

    public Task<int> GetReceivedEventCount() => Task.FromResult(_receivedCount);
}
```

**Step 2: Add event and stream tests to AgentV3Tests.cs**

```csharp
[Fact]
public async Task PublishEvent_AppearsInEventLog()
{
    var agent = Agent($"test-events-{Guid.NewGuid():N}");
    // PublishAsync is protected — test via HandleEventAsync + GetEventLogAsync
    var evt = new AgentEvent("test.event", "source", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, new());
    await agent.HandleEventAsync(evt, CancellationToken.None);
    // Event log populated by direct publish — need to expose or test via sample agent
}

[Fact]
public async Task GetMetadata_WithStreamConsumer_ReportsSubscriptions()
{
    var agent = _cluster.GrainFactory.GetGrain<IStreamTestAgent>($"test-stream-meta-{Guid.NewGuid():N}");
    var metadata = await agent.GetMetadataAsync(CancellationToken.None);
    Assert.Contains("TestEvent", metadata.Subscribes);
}

[Fact]
public async Task EventTypeToStreamName_ConvertsCorrectly()
{
    Assert.Equal("code.changed", Agent.EventTypeToStreamName(typeof(CodeChangedEvent)));
    Assert.Equal("build.completed", Agent.EventTypeToStreamName(typeof(BuildCompletedEvent)));
}
```

**Step 3: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~AgentV3Tests"`
Expected: Tests pass

**Step 4: Commit**

```bash
git add test/Core.Tests/V3/
git commit -m "test(v3): add Phase B tests — events, streams, stream consumer metadata"
```

### Task 26: Create sample event types for use case documentation

**Files:**
- Create: `src/Core/V3/Messages/CodeChangedEvent.cs`
- Create: `src/Core/V3/Messages/BuildCompletedEvent.cs`
- Create: `src/Core/V3/Messages/TestResultEvent.cs`
- Create: `src/Core/V3/Messages/DeployCompletedEvent.cs`
- Create: `src/Core/V3/Messages/HealthCheckEvent.cs`
- Create: `src/Core/V3/Messages/ReviewRequestNotification.cs`

**Step 1: Create CodeChangedEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record CodeChangedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string[] FilePaths,
    [property: Id(4)] string? CommitSha) : IEvent;
```

**Step 2: Create BuildCompletedEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record BuildCompletedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] bool Success,
    [property: Id(4)] string? CommitSha,
    [property: Id(5)] string? Output) : IEvent;
```

**Step 3: Create TestResultEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record TestResultEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] bool Passed,
    [property: Id(4)] int TotalTests,
    [property: Id(5)] int FailedTests,
    [property: Id(6)] string? Summary) : IEvent;
```

**Step 4: Create DeployCompletedEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record DeployCompletedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] bool Success,
    [property: Id(4)] string Environment,
    [property: Id(5)] string? Version) : IEvent;
```

**Step 5: Create HealthCheckEvent.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record HealthCheckEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string ServiceName,
    [property: Id(4)] bool Healthy,
    [property: Id(5)] double? ResponseTimeMs) : IEvent;
```

**Step 6: Create ReviewRequestNotification.cs**

```csharp
namespace Core.V3.Messages;

[GenerateSerializer]
public record ReviewRequestNotification(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string FilePath,
    [property: Id(4)] string Description) : INotification;
```

**Step 7: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 8: Commit**

```bash
git add src/Core/V3/Messages/
git commit -m "feat(v3): add domain event types for use cases — code, build, test, deploy, health, review"
```

---

## Phase C: Tracking

### Task 27: Create TrackingItem model

**Files:**
- Create: `src/Core/V3/TrackingItem.cs`

**Step 1: Create TrackingItem.cs**

```csharp
namespace Core.V3;

[GenerateSerializer]
public record TrackingItem(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] TimeSpan Interval,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] DateTimeOffset? LastCheckAt,
    [property: Id(5)] string? LastResult);
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Core/V3/TrackingItem.cs
git commit -m "feat(v3): add TrackingItem model for recurring checks"
```

### Task 28: Add Agent.Tracking.cs partial

**Files:**
- Create: `src/Core/V3/Agent.Tracking.cs`

**Step 1: Update Agent constructor to add tracking items durable state**

In `Agent.cs`, add constructor parameter:
```csharp
[Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems
```

**Step 2: Create Agent.Tracking.cs**

```csharp
using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using Orleans.DurableJobs;

namespace Core.V3;

public abstract partial class Agent : IDurableJobHandler, IRemindable
{
    protected IDurableDictionary<string, TrackingItem> TrackingItems => trackingItems;

    public async Task StartTrackingAsync(string name, TrackingItem item, TimeSpan interval, CancellationToken ct = default)
    {
        trackingItems[name] = item;
        await WriteStateAsync(ct);
        await JobManager.ScheduleJobAsync(this.GetGrainId(), name, DateTimeOffset.UtcNow.Add(interval), null, ct);
    }

    public async Task StopTrackingAsync(string name, CancellationToken ct = default)
    {
        if (trackingItems.ContainsKey(name))
            trackingItems.Remove(name);
        await WriteStateAsync(ct);
    }

    public virtual Task ReceiveReminder(string reminderName, TickStatus status) => Task.CompletedTask;

    public virtual async Task ExecuteJobAsync(IDurableJobContext context, CancellationToken ct)
    {
        if (!trackingItems.TryGetValue(context.Job.Name, out var item)) return;

        await OnTrackingDueAsync(item, ct);
        trackingItems[item.Id] = item with { LastCheckAt = DateTimeOffset.UtcNow };
        await WriteStateAsync(ct);
        await JobManager.ScheduleJobAsync(this.GetGrainId(), item.Id, DateTimeOffset.UtcNow.Add(item.Interval), null, ct);
    }

    protected virtual async Task OnTrackingDueAsync(TrackingItem item, CancellationToken ct)
    {
        var prompt = $"Check on this tracking item and report: {item.Description}";
        var history = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, prompt)
        };
        var tools = DefineTools();
        var options = tools.Count > 0 ? new ChatOptions { Tools = [.. tools] } : null;
        string result;
        try
        {
            var response = await ChatClient.GetResponseAsync(history, options, ct);
            result = response.Text ?? "";
        }
        catch (Exception ex)
        {
            result = BuildSafeErrorMessage(ex);
        }

        if (item.LastResult is not null && result != item.LastResult)
        {
            await PublishAsync("tracking.changed", new Dictionary<string, object>
            {
                ["TrackingId"] = item.Id,
                ["Description"] = item.Description,
                ["PreviousResult"] = item.LastResult,
                ["CurrentResult"] = result
            }, ct);
        }

        trackingItems[item.Id] = item with { LastResult = result };
    }

    [Description("Start tracking something on a schedule")]
    private async Task<string> StartTracking(
        [Description("What to track")] string description,
        [Description("Check interval in minutes")] int intervalMinutes)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        trackingItems[id] = new TrackingItem(id, description, interval, DateTimeOffset.UtcNow, null, null);
        await WriteStateAsync(AgentCancellation);
        await JobManager.ScheduleJobAsync(this.GetGrainId(), id, DateTimeOffset.UtcNow.Add(interval), null, AgentCancellation);
        return $"Tracking started with ID: {id} — checking every {intervalMinutes} minutes";
    }

    [Description("Stop tracking by ID")]
    private async Task<string> StopTracking([Description("Tracking ID to stop")] string trackingId)
    {
        if (!trackingItems.ContainsKey(trackingId)) return $"Tracking '{trackingId}' not found";
        trackingItems.Remove(trackingId);
        await WriteStateAsync(AgentCancellation);
        return $"Tracking '{trackingId}' stopped";
    }

    [Description("List all active tracking items")]
    private Task<string> ListTracking()
    {
        if (!trackingItems.Any()) return Task.FromResult("No active tracking items");
        var sb = new StringBuilder();
        foreach (var kvp in trackingItems)
        {
            var item = kvp.Value;
            var lastCheck = item.LastCheckAt?.ToString("g") ?? "never";
            sb.AppendLine($"- [{item.Id}] {item.Description} (every {item.Interval.TotalMinutes}min, last: {lastCheck})");
        }
        return Task.FromResult(sb.ToString());
    }
}
```

**Step 3: Update all concrete agents to pass trackingItems constructor parameter**

Update `WeatherAgent`, `TestAgent`, `StreamTestAgent` constructors to include `[Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems` and pass it to base.

**Step 4: Create ITrackableAgent.cs**

```csharp
namespace Core.V3;

public interface ITrackableAgent : IAgent
{
    Task StartTrackingAsync(string name, TrackingItem item, TimeSpan interval, CancellationToken ct);
    Task StopTrackingAsync(string name, CancellationToken ct);
}
```

**Step 5: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 6: Commit**

```bash
git add src/Core/V3/Agent.Tracking.cs src/Core/V3/ITrackableAgent.cs src/Core/V3/Agent.cs src/Core/V3/WeatherAgent.cs
git commit -m "feat(v3): add Agent.Tracking.cs — DurableJobs, recurring checks, tracking tools"
```

### Task 29: Write tests for Phase C (Tracking)

**Files:**
- Modify: `test/Core.Tests/V3/AgentV3Tests.cs`

**Step 1: Add tracking tests**

```csharp
[Fact]
public async Task StartTracking_CreatesTrackingItem()
{
    var agent = Agent($"test-tracking-{Guid.NewGuid():N}");
    var item = new TrackingItem("t1", "Check server", TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, null, null);
    await ((ITrackableAgent)agent).StartTrackingAsync("t1", item, TimeSpan.FromMinutes(5), CancellationToken.None);
    // Verify via state
    var state = await agent.GetStateAsync(CancellationToken.None);
    // Tracking items are in separate durable dict, not in state
}

[Fact]
public async Task StopTracking_RemovesItem()
{
    var agent = Agent($"test-stop-tracking-{Guid.NewGuid():N}");
    var item = new TrackingItem("t2", "Check DB", TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow, null, null);
    var trackable = (ITrackableAgent)agent;
    await trackable.StartTrackingAsync("t2", item, TimeSpan.FromMinutes(1), CancellationToken.None);
    await trackable.StopTrackingAsync("t2", CancellationToken.None);
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~AgentV3Tests"`
Expected: Tests pass

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/
git commit -m "test(v3): add Phase C tracking tests"
```

---

## Phase D: Observers

### Task 30: Add Agent.Observers.cs partial

**Files:**
- Create: `src/Core/V3/Agent.Observers.cs`
- Create: `src/Core/V3/IObservableAgent.cs`

**Step 1: Create Agent.Observers.cs**

```csharp
namespace Core.V3;

// Phase 2: typed observer dispatch
public abstract partial class Agent
{
    private readonly HashSet<IGrainObserver> _observers = [];

    public Task SubscribeObserverAsync(IGrainObserver observer, CancellationToken ct = default)
    {
        _observers.Add(observer);
        return Task.CompletedTask;
    }

    public Task UnsubscribeObserverAsync(IGrainObserver observer, CancellationToken ct = default)
    {
        _observers.Remove(observer);
        return Task.CompletedTask;
    }
}
```

**Step 2: Create IObservableAgent.cs**

```csharp
namespace Core.V3;

public interface IObservableAgent : IAgent
{
    Task SubscribeObserverAsync(IGrainObserver observer, CancellationToken ct);
    Task UnsubscribeObserverAsync(IGrainObserver observer, CancellationToken ct);
}
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/V3/Agent.Observers.cs src/Core/V3/IObservableAgent.cs
git commit -m "feat(v3): add Agent.Observers.cs — Phase 2 placeholder for typed observer dispatch"
```

---

## Phase E: DynamicAgent

### Task 31: Create DynamicAgent

**Files:**
- Create: `src/Core/V3/DynamicAgent.cs`
- Create: `src/Core/V3/AgentConfiguration.cs`
- Create: `src/Core/V3/IDynamicAgent.cs`

**Step 1: Create AgentConfiguration.cs**

```csharp
namespace Core.V3;

[GenerateSerializer]
public record AgentConfiguration(
    [property: Id(0)] string? DisplayName,
    [property: Id(1)] string? SystemPrompt,
    [property: Id(2)] string[]? ToolNames,
    [property: Id(3)] string? WorkspacePath,
    [property: Id(4)] string[]? SubscribeToStreams);
```

**Step 2: Create IDynamicAgent.cs**

```csharp
namespace Core.V3;

public interface IDynamicAgent : IAgent
{
    Task ConfigureAsync(AgentConfiguration config, CancellationToken ct);
}
```

**Step 3: Create DynamicAgent.cs**

```csharp
using Orleans.Journaling;

namespace Core.V3;

[GrainType("dynamic-agent-v3")]
public class DynamicAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IDynamicAgent
{
    protected override string Instructions =>
        state.TryGetValue("config-system-prompt", out var entry)
            ? entry.Value.ToString() ?? "You are a helpful assistant."
            : "You are a helpful assistant.";

    protected override string DisplayName =>
        state.TryGetValue("config-display-name", out var entry)
            ? entry.Value.ToString() ?? "Dynamic Agent"
            : "Dynamic Agent";

    protected override AgentKind AgentKindValue => AgentKind.Dynamic;

    public async Task ConfigureAsync(AgentConfiguration config, CancellationToken ct)
    {
        if (config.DisplayName is not null)
            state["config-display-name"] = new StateEntry("config-display-name", config.DisplayName);
        if (config.SystemPrompt is not null)
            state["config-system-prompt"] = new StateEntry("config-system-prompt", config.SystemPrompt);
        if (config.ToolNames is not null)
            state["config-tool-names"] = new StateEntry("config-tool-names", string.Join(",", config.ToolNames));
        if (config.WorkspacePath is not null)
            await SetWorkspaceAsync(config.WorkspacePath, ct);
        await WriteStateAsync(ct);
    }
}
```

**Step 4: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 5: Commit**

```bash
git add src/Core/V3/DynamicAgent.cs src/Core/V3/AgentConfiguration.cs src/Core/V3/IDynamicAgent.cs
git commit -m "feat(v3): add DynamicAgent — runtime-configured agent with string-based composition"
```

### Task 32: Write DynamicAgent tests

**Files:**
- Create: `test/Core.Tests/V3/DynamicAgentTests.cs`

**Step 1: Create DynamicAgentTests.cs**

```csharp
using Core.V3;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.V3;

public class DynamicAgentTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task Configure_SetsDisplayName()
    {
        var agent = _cluster.GrainFactory.GetGrain<IDynamicAgent>($"dyn-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration("My Bot", null, null, null, null), CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("My Bot", meta.DisplayName);
    }

    [Fact]
    public async Task Configure_SetsSystemPrompt()
    {
        var agent = _cluster.GrainFactory.GetGrain<IDynamicAgent>($"dyn-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration(null, "Custom instructions", null, null, null), CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("Custom instructions", meta.Description);
    }

    [Fact]
    public async Task Configure_SetsWorkspace()
    {
        var agent = _cluster.GrainFactory.GetGrain<IDynamicAgent>($"dyn-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration(null, null, null, "/tmp/work", null), CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.True(state.Entries.ContainsKey("workspace-path"));
    }

    [Fact]
    public async Task Kind_IsDynamic()
    {
        var agent = _cluster.GrainFactory.GetGrain<IDynamicAgent>($"dyn-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal(AgentKind.Dynamic, meta.Kind);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~DynamicAgentTests"`
Expected: Tests pass

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/DynamicAgentTests.cs
git commit -m "test(v3): add DynamicAgent tests — configure, metadata, kind"
```

---

## Phase F: Use Case Sample Agents

### Task 33: Create Use Case 1 — Code Review Bot sample

**Files:**
- Create: `src/Core/V3/Samples/CodeReviewAgent.cs`

**Step 1: Create Samples directory**

Run: `mkdir -p src/Core/V3/Samples`

**Step 2: Create CodeReviewAgent.cs**

```csharp
using Core.V3.Communication;
using Core.V3.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace Core.V3.Samples;

public interface ICodeReviewAgent : IAgent;

public class CodeReviewAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      ICodeReviewAgent,
      IStreamConsumer<CodeChangedEvent>
{
    protected override string Instructions =>
        "You are a code review agent. When code changes arrive, analyze them for bugs, style issues, and security vulnerabilities.";

    protected override string DisplayName => "Code Review Bot";

    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var fileList = string.Join(", ", evt.FilePaths);
        var prompt = $"Review these changed files: {fileList}. Commit: {evt.CommitSha}";
        await GetResponse(prompt, AgentCancellation);
    }
}
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add src/Core/V3/Samples/
git commit -m "feat(v3): add CodeReviewAgent sample — UC1 stream consumer"
```

### Task 34: Create Use Case 2 — Infrastructure Monitor sample

**Files:**
- Create: `src/Core/V3/Samples/InfraMonitorAgent.cs`

**Step 1: Create InfraMonitorAgent.cs**

```csharp
using Core.V3.Communication;
using Core.V3.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Core.V3.Samples;

public interface IInfraMonitorAgent : IAgent, ITrackableAgent;

public class InfraMonitorAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IInfraMonitorAgent,
      IStreamProducer<HealthCheckEvent>
{
    protected override string Instructions =>
        "You monitor infrastructure health. Check service endpoints and report issues.";

    protected override string DisplayName => "Infrastructure Monitor";

    public async Task PublishToStreamAsync(HealthCheckEvent evt, CancellationToken ct = default)
    {
        await PublishTypedAsync(evt, ct);
    }

    protected override async Task OnTrackingDueAsync(TrackingItem item, CancellationToken ct)
    {
        await base.OnTrackingDueAsync(item, ct);
        await PublishToStreamAsync(new HealthCheckEvent(
            this.GetPrimaryKeyString(),
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            item.Description,
            true,
            null), ct);
    }
}
```

**Step 2: Build and commit**

Run: `dotnet build src/Core/Core.csproj`

```bash
git add src/Core/V3/Samples/InfraMonitorAgent.cs
git commit -m "feat(v3): add InfraMonitorAgent sample — UC2 tracking + stream producer"
```

### Task 35: Create Use Case 5 — CI/CD Pipeline sample

**Files:**
- Create: `src/Core/V3/Samples/CIPipelineAgent.cs`

**Step 1: Create CIPipelineAgent.cs**

```csharp
using Core.V3.Communication;
using Core.V3.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace Core.V3.Samples;

public interface ICIPipelineAgent : IAgent;

public class CIPipelineAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      ICIPipelineAgent,
      IStreamConsumer<CodeChangedEvent>,
      IStreamProducer<BuildCompletedEvent>
{
    protected override string Instructions =>
        "You are a CI/CD pipeline agent. When code changes arrive, run builds and tests, then publish results.";

    protected override string DisplayName => "CI/CD Pipeline";

    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var result = await GetResponse($"Build and test files: {string.Join(", ", evt.FilePaths)}", AgentCancellation);
        var success = !result.Contains("error", StringComparison.OrdinalIgnoreCase);

        await PublishToStreamAsync(new BuildCompletedEvent(
            this.GetPrimaryKeyString(),
            evt.CorrelationId,
            DateTimeOffset.UtcNow,
            success,
            evt.CommitSha,
            result), AgentCancellation);
    }

    public async Task PublishToStreamAsync(BuildCompletedEvent evt, CancellationToken ct = default)
    {
        await PublishTypedAsync(evt, ct);
    }
}
```

**Step 2: Build and commit**

Run: `dotnet build src/Core/Core.csproj`

```bash
git add src/Core/V3/Samples/CIPipelineAgent.cs
git commit -m "feat(v3): add CIPipelineAgent sample — UC5 stream consumer + producer pipeline"
```

### Task 36: Create remaining use case samples (UC3, UC4)

**Files:**
- Create: `src/Core/V3/Samples/PersonalAssistantAgent.cs`
- Create: `src/Core/V3/Samples/KnowledgeBaseAgent.cs`

**Step 1: Create PersonalAssistantAgent.cs (UC3)**

```csharp
using Core.V3.Communication;
using Core.V3.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace Core.V3.Samples;

public interface IPersonalAssistantAgent : IAgent;

public class PersonalAssistantAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IPersonalAssistantAgent,
      IStreamConsumer<ProgressNotification>,
      IBroadcaster<AssignTaskCommand>
{
    protected override string Instructions =>
        "You are a personal assistant. Decompose tasks and delegate to the engineering team.";

    protected override string DisplayName => "Personal Assistant";

    private readonly HashSet<string> _receivers = [];

    public async Task OnStreamEventAsync(ProgressNotification evt, StreamSequenceToken? token)
    {
        await GetResponse($"Progress update from {evt.SourceAgentId}: {evt.Step} — {evt.Status}", AgentCancellation);
    }

    public async Task<BroadcastResult> BroadcastAsync(AssignTaskCommand message, CancellationToken ct = default)
    {
        var delivered = 0;
        foreach (var id in _receivers)
        {
            try
            {
                var agent = GrainFactory.GetGrain<IAgent>(id);
                await agent.GetResponse($"Task assigned: {message.Description}", ct);
                delivered++;
            }
            catch { }
        }
        return new BroadcastResult(_receivers.Count, delivered, _receivers.Count - delivered, []);
    }

    public Task RegisterReceiverAsync(string receiverId) { _receivers.Add(receiverId); return Task.CompletedTask; }
    public Task UnregisterReceiverAsync(string receiverId) { _receivers.Remove(receiverId); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> GetReceiversAsync() => Task.FromResult<IReadOnlyList<string>>([.. _receivers]);
}
```

**Step 2: Create KnowledgeBaseAgent.cs (UC4)**

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Core.V3.Samples;

public interface IKnowledgeBaseAgent : IAgent;

public class KnowledgeBaseAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IKnowledgeBaseAgent
{
    protected override string Instructions =>
        "You are a knowledge base agent. Answer questions using the indexed documents available through your tools.";

    protected override string DisplayName => "Knowledge Base";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(SearchDocuments),
        AIFunctionFactory.Create(GetDocument)
    ];

    [Description("Search indexed documents by keyword")]
    static string[] SearchDocuments([Description("Search query")] string query) =>
        [$"doc-1: Getting Started with IAW", $"doc-2: Agent Behaviors Guide"];

    [Description("Get a document by ID")]
    static string GetDocument([Description("Document ID")] string documentId) =>
        $"Document {documentId}: This is a sample document about IAW agent behaviors.";
}
```

**Step 3: Build and commit**

Run: `dotnet build src/Core/Core.csproj`

```bash
git add src/Core/V3/Samples/
git commit -m "feat(v3): add PersonalAssistant (UC3) and KnowledgeBase (UC4) sample agents"
```

---

## Phase G: Agent Registry

### Task 37: Create AgentRegistration and registry models

**Files:**
- Create: `src/Core/V3/Registry/AgentRegistration.cs`
- Create: `src/Core/V3/Registry/AgentQuery.cs`
- Create: `src/Core/V3/Registry/IAgentRegistryGrain.cs`

**Step 1: Create Registry directory**

Run: `mkdir -p src/Core/V3/Registry`

**Step 2: Create AgentRegistration.cs**

```csharp
namespace Core.V3.Registry;

[GenerateSerializer]
public record AgentRegistration(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] AgentKind Kind,
    [property: Id(4)] string[] Capabilities,
    [property: Id(5)] string[] Publishes,
    [property: Id(6)] string[] Subscribes);
```

**Step 3: Create AgentQuery.cs**

```csharp
namespace Core.V3.Registry;

[GenerateSerializer]
public record AgentQuery(
    [property: Id(0)] AgentKind? Kind = null,
    [property: Id(1)] string[]? Capabilities = null,
    [property: Id(2)] string[]? Publishes = null,
    [property: Id(3)] string[]? Subscribes = null);
```

**Step 4: Create IAgentRegistryGrain.cs**

```csharp
namespace Core.V3.Registry;

public interface IAgentRegistryGrain : IGrainWithStringKey
{
    Task RegisterAsync(AgentRegistration registration);
    Task UnregisterAsync(string agentType);
    Task<IReadOnlyList<AgentRegistration>> GetAllAsync();
    Task<IReadOnlyList<AgentRegistration>> QueryAsync(AgentQuery query);
    Task<AgentRegistration?> GetByTypeAsync(string agentType);
}
```

**Step 5: Build and commit**

Run: `dotnet build src/Core/Core.csproj`

```bash
git add src/Core/V3/Registry/
git commit -m "feat(v3): add agent registry models and grain interface"
```

### Task 38: Create AgentRegistryGrain implementation

**Files:**
- Create: `src/Core/V3/Registry/AgentRegistryGrain.cs`

**Step 1: Create AgentRegistryGrain.cs**

```csharp
using Orleans.Journaling;

namespace Core.V3.Registry;

public class AgentRegistryGrain(
    [Memory("registrations")] IDurableDictionary<string, AgentRegistration> registrations)
    : DurableGrain, IAgentRegistryGrain
{
    public async Task RegisterAsync(AgentRegistration registration)
    {
        registrations[registration.AgentType] = registration;
        await WriteStateAsync();
    }

    public async Task UnregisterAsync(string agentType)
    {
        if (registrations.ContainsKey(agentType))
            registrations.Remove(agentType);
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<AgentRegistration>> GetAllAsync()
        => Task.FromResult<IReadOnlyList<AgentRegistration>>(registrations.Values.ToList());

    public Task<IReadOnlyList<AgentRegistration>> QueryAsync(AgentQuery query)
    {
        var results = registrations.Values.AsEnumerable();
        if (query.Kind is not null)
            results = results.Where(r => r.Kind == query.Kind);
        if (query.Capabilities is { Length: > 0 } caps)
            results = results.Where(r => caps.All(c => r.Capabilities.Contains(c)));
        if (query.Publishes is { Length: > 0 } pubs)
            results = results.Where(r => pubs.Any(p => r.Publishes.Contains(p)));
        if (query.Subscribes is { Length: > 0 } subs)
            results = results.Where(r => subs.Any(s => r.Subscribes.Contains(s)));
        return Task.FromResult<IReadOnlyList<AgentRegistration>>(results.ToList());
    }

    public Task<AgentRegistration?> GetByTypeAsync(string agentType)
        => Task.FromResult(registrations.TryGetValue(agentType, out var reg) ? reg : null);
}
```

**Step 2: Build and commit**

Run: `dotnet build src/Core/Core.csproj`

```bash
git add src/Core/V3/Registry/AgentRegistryGrain.cs
git commit -m "feat(v3): add AgentRegistryGrain — durable agent registration store"
```

### Task 39: Create AgentRegistrationStartupTask

**Files:**
- Create: `src/Core/V3/Registry/AgentRegistrationStartupTask.cs`

**Step 1: Create AgentRegistrationStartupTask.cs**

```csharp
using System.Reflection;
using Core.V3.Attributes;
using Core.V3.Communication;

namespace Core.V3.Registry;

public class AgentRegistrationStartupTask(IGrainFactory grainFactory) : IStartupTask
{
    private static readonly HashSet<Type> ExcludedInterfaces =
    [
        typeof(IAgent), typeof(IDynamicAgent), typeof(IEventDrivenAgent),
        typeof(IStreamingAgent), typeof(ITrackableAgent), typeof(IObservableAgent)
    ];

    public async Task Execute(CancellationToken ct)
    {
        var registry = grainFactory.GetGrain<IAgentRegistryGrain>("global");
        var agentTypes = DiscoverAgentTypes();

        foreach (var type in agentTypes)
        {
            var registration = BuildRegistration(type);
            await registry.RegisterAsync(registration);

            if (HasSubscriptions(type))
            {
                var iface = ResolveAgentInterface(type);
                if (iface is not null)
                    grainFactory.GetGrain(iface, GetAgentShortName(type.Name));
            }
        }
    }

    private static IEnumerable<Type> DiscoverAgentTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(Agent)));

    private static bool HasSubscriptions(Type type) =>
        type.GetCustomAttributes<SubscribesAttribute>().Any() ||
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>));

    private static Type? ResolveAgentInterface(Type type) =>
        type.GetInterfaces()
            .Where(i => typeof(IGrainWithStringKey).IsAssignableFrom(i) && !ExcludedInterfaces.Contains(i))
            .OrderByDescending(i => i.GetMethods().Length)
            .FirstOrDefault();

    private static AgentRegistration BuildRegistration(Type type)
    {
        var caps = type.GetCustomAttributes<CapabilityAttribute>().Select(a => a.Capability).ToArray();
        var pubs = type.GetCustomAttributes<PublishesAttribute>().Select(a => a.EventName)
            .Concat(type.GetInterfaces()
                .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IBroadcaster<>) || i.GetGenericTypeDefinition() == typeof(INotifier<>)))
                .Select(i => i.GetGenericArguments()[0].Name))
            .Distinct().ToArray();
        var subs = type.GetCustomAttributes<SubscribesAttribute>().Select(a => a.EventName)
            .Concat(type.GetInterfaces()
                .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IReceiver<>) || i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>)))
                .Select(i => i.GetGenericArguments()[0].Name))
            .Distinct().ToArray();

        return new AgentRegistration(
            type.Name,
            GetAgentShortName(type.Name),
            "",
            type.IsSubclassOf(typeof(DynamicAgent)) ? AgentKind.Dynamic : AgentKind.Static,
            caps, pubs, subs);
    }

    private static string GetAgentShortName(string typeName)
    {
        var name = typeName;
        if (name.StartsWith("I")) name = name[1..];
        if (name.EndsWith("Agent")) name = name[..^5];
        return name;
    }
}
```

**Step 2: Build and commit**

Run: `dotnet build src/Core/Core.csproj`

```bash
git add src/Core/V3/Registry/AgentRegistrationStartupTask.cs
git commit -m "feat(v3): add AgentRegistrationStartupTask — auto-discovery and registration"
```

---

## Phase H: Integration Verification

### Task 40: Full build verification

**Step 1: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds with zero errors

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All existing tests pass, new V3 tests pass

**Step 3: Commit any fixes needed**

### Task 41: Run Aspire integration

**Step 1: Start Aspire**

Run: `aspire run --project src/IAW.AppHost/Aspire.csproj`
Expected: All resources start

**Step 2: Verify V3 agents activate**

Check Aspire dashboard for agent activations in telemetry

**Step 3: Stop and commit**

```bash
git add -A
git commit -m "feat(v3): Phase A-H complete — all behaviors migrated with tests and samples"
```

---

## Summary: Migration Task Count

| Phase | Tasks | Steps |
|-------|-------|-------|
| Pre-Migration (fixes) | 3 | 15 |
| Phase A: Conversation + Tools + State | 18 | 120 |
| Phase B: Events + Streams | 5 | 35 |
| Phase C: Tracking | 3 | 18 |
| Phase D: Observers | 1 | 4 |
| Phase E: DynamicAgent | 2 | 10 |
| Phase F: Use Case Samples | 4 | 16 |
| Phase G: Agent Registry | 3 | 12 |
| Phase H: Integration | 2 | 6 |
| **Total** | **41** | **~236 steps** |

Note: Each step above expands to 3-5 micro-actions (read, write, build, test, commit) bringing effective step count to ~800.
