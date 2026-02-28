# Building Agents

This guide covers creating agents, adding LLM support, defining tools, managing state, and using the tracking system.

## Minimal Agent

Every agent extends the `Agent` base class with six durable state constructor parameters:

```csharp
using Core;
using Orleans.Journaling;

public class MinimalAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking);
```

This gives you a fully functional agent with durable state, history, events, notifications, tracking, tools, and streaming -- all inherited from the base class.

## Virtual Properties

Override these properties to customize agent behavior:

```csharp
public class AssistantAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    // Grain primary key, read-only
    // public string Id => this.GetPrimaryKeyString();

    public override string DisplayName => "Personal Assistant";

    public override string SystemPrompt =>
        "You are a helpful personal assistant. Be concise and accurate.";
}
```

| Property | Type | Default |
|---|---|---|
| `Id` | `string` | `this.GetPrimaryKeyString()` |
| `DisplayName` | `string` | Same as `Id` |
| `SystemPrompt` | `string` | `string.Empty` |

## Adding LLM Support

To make an agent LLM-powered, call the `Activate` method with an `IChatClient` instance:

```csharp
public virtual void Activate(IChatClient chatClient)
```

This converts the `IChatClient` into an `AIAgent` (from `Microsoft.Agents.AI`) using the agent's `SystemPrompt`, `Id`, `DisplayName`, and tools from `DefineTools()`. The resulting `AIAgent` is stored in the protected `Llm` property.

```csharp
// In your silo startup or grain activation
var chatClient = serviceProvider.GetRequiredService<IChatClient>();
var agent = grainFactory.GetGrain<IAgent>("my-assistant");

// From within the grain itself
Activate(chatClient);
```

Once activated, use `SendAsync` to stream LLM responses:

```csharp
await foreach (var token in agent.SendAsync("Summarize the project status"))
{
    Console.Write(token);
}
```

`SendAsync` is an `IAsyncEnumerable<string>` that:
1. Records the user message to durable history
2. Streams tokens from the LLM via `Llm.RunStreamingAsync()`
3. Records the complete assistant response to history
4. Emits OpenTelemetry activity spans and metrics

## Defining Tools

Override `DefineTools()` to expose tools the LLM can call:

```csharp
public override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(SearchKnowledgeBase, "search",
        "Search the knowledge base for relevant information"),
    AIFunctionFactory.Create(CreateReminder, "create_reminder",
        "Create a reminder for a future date")
];

private async Task<string> SearchKnowledgeBase(string query)
{
    // implementation
    return $"Results for: {query}";
}

private async Task<string> CreateReminder(string text, DateTime dueDate)
{
    await SetStateAsync($"reminder-{Guid.NewGuid():N}", text);
    return $"Reminder set for {dueDate:g}";
}
```

Tools are discovered by `InvokeToolAsync`, which finds the matching `AIFunction` by name and calls it:

```csharp
var result = await agent.InvokeToolAsync("search", new Dictionary<string, string>
{
    ["query"] = "project status"
});
```

## State Management

The agent's key-value state is stored in `IDurableDictionary<string, string>`. All mutations are persisted via `WriteStateAsync()`.

### Set and get values

```csharp
await agent.SetStateAsync("user-name", "Alice");
var name = await agent.GetStateValueAsync("user-name");
```

### Get all state

```csharp
var allState = await agent.GetStateAsync();
foreach (var (key, value) in allState)
{
    Console.WriteLine($"{key} = {value}");
}
```

### Increment counters

`IncrementAsync` parses the current value as an integer (defaulting to 0), adds 1, and returns the new value:

```csharp
var loginCount = await agent.IncrementAsync("login-count");
Console.WriteLine($"Login #{loginCount}");
```

## History

Conversation history is stored as a durable list of `AgentHistoryEntry` records (role, content, timestamp). History is automatically managed by `SendAsync`, but you can also add entries manually:

```csharp
await agent.AddHistoryAsync("system", "Agent initialized at startup");

var history = await agent.GetHistoryAsync();
foreach (var entry in history)
{
    Console.WriteLine($"[{entry.TimestampUtc:u}] {entry.Role}: {entry.Content}");
}
```

Each history entry is also published to the `"agent-history"` Orleans stream for real-time subscribers.

## Events

Record named events with optional JSON payloads:

```csharp
await agent.PublishEventAsync("task-completed", "{\"taskId\":\"abc\",\"duration\":42}");

var events = await agent.GetEventsAsync();
```

Events are persisted durably and published to the `"agent-events"` Orleans stream.

## Tracking

The tracking system schedules periodic ticks for monitoring or polling tasks. It uses Orleans reminders for intervals >= 1 minute (silo-crash-safe) and grain timers for shorter intervals.

```csharp
// Tick every 5 minutes, stop after 10 ticks
await agent.StartTrackingAsync(TimeSpan.FromMinutes(5), maxTicks: 10);

// Check tracking state
var status = await agent.GetTrackingStatusAsync();
Console.WriteLine($"Tracking: {status.IsTracking}, Ticks: {status.TickCount}/{status.MaxTicks}");

// Stop tracking early
await agent.StopTrackingAsync();
```

The `AgentTrackingStatus` record contains:

| Property | Type | Description |
|---|---|---|
| `IsTracking` | `bool` | Whether tracking is currently active |
| `TickCount` | `int` | Number of ticks completed |
| `StartedAtUtc` | `DateTimeOffset?` | When tracking started |
| `Interval` | `TimeSpan` | Time between ticks |
| `MaxTicks` | `int` | Maximum ticks before auto-stop |

On grain reactivation, `OnActivateAsync` resumes tracking if the status indicates it should still be running and the tick count has not reached the maximum.

## Streams

Publish messages to Orleans streams for real-time communication:

```csharp
await agent.PublishStreamAsync("my-namespace", streamId, "Hello from agent!");
```

The agent uses the `"agents"` stream provider configured by `AddIAW()` in the Aspire AppHost.

## Overriding GetMetadataAsync

Customize the metadata returned about your agent:

```csharp
public override Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    return Task.FromResult(new AgentMetadata
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = DisplayName,
        Capabilities = ["state", "history", "events", "notifications",
                        "tracking", "streams", "tools", "code-analysis"]
    });
}
```
