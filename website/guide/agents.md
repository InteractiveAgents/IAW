# Building Agents

This guide covers creating V3 agents: the constructor parameters, override points, custom tools, behavior interfaces, and testing.

## Minimal Agent

Every agent extends the `Agent` base class and implements a grain interface that extends `IAgent`:

```csharp
using Core.V3;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface IMinimalAgent : IAgent;

public class MinimalAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IMinimalAgent
{
    protected override string Instructions => "You are a minimal agent.";
}
```

This gives you a fully functional agent with durable conversation history, state management, event publishing, tracking, and built-in tools.

## Constructor Parameters

The five constructor parameters are injected by Orleans:

| Parameter | Type | Purpose |
|---|---|---|
| `state` | `IDurableDictionary<string, StateEntry>` | Key-value state store (workspace path, custom data) |
| `eventLog` | `IDurableList<AgentEvent>` | Append-only event log |
| `chatClient` | `IChatClient` | LLM provider from Microsoft.Extensions.AI |
| `history` | `IDurableList<ChatMessage>` | Conversation history |
| `trackingItems` | `IDurableDictionary<string, TrackingItem>` | Scheduled tracking items |

::: tip
You never instantiate these yourself. Orleans resolves the `[Memory]`-annotated parameters from journaled grain storage and the `IChatClient` from dependency injection.
:::

## Override Points

`Agent` exposes four virtual members:

| Member | Default | Purpose |
|---|---|---|
| `Instructions` | `"You are a helpful AI assistant..."` | LLM system prompt |
| `DisplayName` | `GetType().Name` | Human-readable name for metadata |
| `DefineTools()` | Empty list | Custom AI tools for the LLM |
| `OnTrackingDueAsync()` | LLM-powered check | Handle tracking item due events |

### Instructions

The system prompt sent to the LLM on every conversation turn:

```csharp
protected override string Instructions =>
    "You are a code review expert. Analyze code for bugs, security issues, and style.";
```

### DisplayName

Used in metadata and the agent registry:

```csharp
protected override string DisplayName => "Code Review Bot";
```

### DefineTools

Override to add custom tools the LLM can call. Use `AIFunctionFactory.Create()`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(SearchKnowledgeBase),
    AIFunctionFactory.Create(CreateReminder)
];

[Description("Search the knowledge base for relevant information")]
private async Task<string> SearchKnowledgeBase(
    [Description("Search query")] string query)
{
    return $"Results for: {query}";
}

[Description("Create a reminder for a future date")]
private async Task<string> CreateReminder(
    [Description("Reminder text")] string text,
    [Description("Due date")] DateTime dueDate)
{
    State[$"reminder-{Guid.NewGuid():N}"] = new StateEntry("reminder", text);
    await WriteStateAsync(AgentCancellation);
    return $"Reminder set for {dueDate:g}";
}
```

::: warning
Tool methods must have a `[Description]` attribute. Without it, the method won't be discovered by the tool registration system.
:::

## Conversation

The agent provides two conversation methods:

```csharp
// Single response
var response = await agent.GetResponse("What's the weather?", ct);

// Streaming response
await foreach (var chunk in agent.GetResponseStream("Tell me a story", ct))
{
    Console.Write(chunk);
}
```

Conversation history is persisted in the durable `history` list via `DurableChatHistoryProvider`. Clear it with:

```csharp
await agent.ClearHistoryAsync(ct);
```

## State Management

The agent's state is a durable dictionary of `StateEntry` records:

```csharp
// Set workspace (enables FileTools and ShellTools)
await agent.SetWorkspaceAsync("/path/to/project", ct);

// Read all state
var state = await agent.GetStateAsync(ct);
foreach (var entry in state.Entries)
{
    Console.WriteLine($"{entry.Key} = {entry.Value.Value}");
}
```

Inside the agent class, access state directly:

```csharp
State["my-key"] = new StateEntry("my-key", "my-value");
await WriteStateAsync(AgentCancellation);
```

## Events

Publish events to the event log and Orleans streams:

```csharp
// Untyped event
await PublishAsync("task.completed", new Dictionary<string, object>
{
    ["taskId"] = "abc",
    ["duration"] = 42
}, ct);

// Typed event (uses IEvent interface)
await PublishTypedAsync(new BuildCompletedEvent(
    SourceAgentId: this.GetPrimaryKeyString(),
    CorrelationId: Guid.NewGuid().ToString(),
    Timestamp: DateTimeOffset.UtcNow,
    Success: true,
    CommitSha: "abc123",
    Output: "Build succeeded"), ct);
```

## Behavior Interfaces

Add communication capabilities by implementing typed interfaces:

```csharp
public class MyAgent : Agent,
    IStreamConsumer<CodeChangedEvent>,    // auto-subscribes to code.changed stream
    IStreamProducer<BuildCompletedEvent>, // can publish build.completed events
    IReceiver<AssignTaskCommand>,         // can receive directed commands
    IBroadcaster<AlertNotification>       // can broadcast to registered receivers
{
    // IStreamConsumer callback -- called automatically
    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var review = await GetResponse($"Review: {string.Join(", ", evt.FilePaths)}", AgentCancellation);
    }

    // IReceiver -- accept directed messages
    public async Task<MessageReceipt> ReceiveAsync(AssignTaskCommand cmd, CancellationToken ct)
    {
        await GetResponse($"Task: {cmd.Description}", ct);
        return new MessageReceipt(true, this.GetPrimaryKeyString(), DateTimeOffset.UtcNow, null);
    }

    public Task<bool> CanReceiveAsync(CancellationToken ct) => Task.FromResult(true);
}
```

See [Events & Streams](/guide/events-streams) for detailed patterns.

## Metadata and Capabilities

The agent automatically reports its metadata based on implemented interfaces and attributes:

```csharp
var metadata = await agent.GetMetadataAsync(ct);
// metadata.AgentType = "MyAgent"
// metadata.DisplayName = "My Agent"
// metadata.Publishes = ["BuildCompletedEvent", "AlertNotification"]
// metadata.Subscribes = ["CodeChangedEvent", "AssignTaskCommand"]

var caps = await agent.GetCapabilitiesAsync(ct);
// caps.HasMemory = true
// caps.HasEvents = true (because it implements IStreamConsumer/IStreamProducer)
// caps.HasTools = true
```

Add custom capabilities with attributes:

```csharp
using Core.V3.Attributes;

[Capability("code-review")]
[Publishes("review.completed")]
[Subscribes("code.changed")]
public class CodeReviewAgent : Agent { ... }
```

## Cancellation

Every agent has a cancellation token accessible via `AgentCancellation`. Cancel an agent externally:

```csharp
await agent.CancelAsync(ct);
```

This cancels the current token and creates a new one, stopping any in-progress LLM calls or tool executions.

## Complete Example: Weather Agent

```csharp
using System.ComponentModel;
using Core.V3;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface IWeatherAgent : IAgent;

public class WeatherAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IWeatherAgent
{
    protected override string Instructions =>
        "You're a weather assistant. Use the available tools to answer questions about weather.";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(GetCurrentWeather),
        AIFunctionFactory.Create(GetForecast)
    ];

    [Description("Gets the current weather for a given city")]
    static WeatherInfo GetCurrentWeather(string city) => new(
        City: city,
        TemperatureCelsius: Random.Shared.Next(-10, 40),
        Condition: PickRandom("Sunny", "Cloudy", "Rainy", "Snowy"),
        Humidity: Random.Shared.Next(20, 100));

    [Description("Gets a 3-day weather forecast for a given city")]
    static List<ForecastDay> GetForecast(string city) =>
    [.. Enumerable.Range(1, 3)
        .Select(i => new ForecastDay(
            Date: DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
            HighCelsius: Random.Shared.Next(15, 40),
            LowCelsius: Random.Shared.Next(-5, 15),
            Condition: PickRandom("Sunny", "Cloudy", "Rainy")))];

    static string PickRandom(params string[] options) =>
        options[Random.Shared.Next(options.Length)];
}

public record WeatherInfo(string City, int TemperatureCelsius, string Condition, int Humidity);
public record ForecastDay(DateOnly Date, int HighCelsius, int LowCelsius, string Condition);
```
