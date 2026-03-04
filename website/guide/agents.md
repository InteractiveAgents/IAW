# Building Agents

This guide covers creating agents with AgentV2, adding LLM support, defining tools, managing memory, and using the scheduling system.

## Minimal Agent

Every agent extends the `AgentV2` base class. The only required override is `Profile`:

```csharp
using Core.V2;

public class MinimalAgent : AgentV2
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Minimal",
        Instructions = string.Empty
    };
}
```

This gives you a fully functional agent with durable messages, memory, events, notifications, scheduling, tools, and streaming -- all inherited from the base class. No constructor boilerplate is needed.

## Override Points

`AgentV2` exposes four virtual methods:

| Method | Signature | Default |
|---|---|---|
| `Profile` | `abstract AgentProfile { get; }` | (required) |
| `OnRespondAsync` | `virtual Task<AgentReply>` | Returns `"Not implemented"` |
| `DefineTools` | `virtual IReadOnlyList<AITool>` | Empty list |
| `OnScheduleTickAsync` | `virtual Task` | No-op |

## Adding LLM Support

Inject an `IChatClient` using the `[Llm<TModel>]` attribute and override `OnRespondAsync` to use the `RespondWithLlmAsync` helper:

```csharp
using Core.AI;
using Core.AI.Models;
using Core.V2;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public class AssistantAgent(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : AgentV2(messages, memory, events, subscriptions, notifications, tracking)
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Assistant",
        Instructions = "You are a helpful personal assistant. Be concise and accurate."
    };

    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
        => RespondWithLlmAsync(chatClient, request, ct);
}
```

`RespondWithLlmAsync` handles the full flow:
1. Builds a `ChatMessage` list from `Profile.Instructions` and the durable message history
2. Includes tools from `DefineTools()` in the `ChatOptions`
3. Calls `IChatClient.GetResponseAsync`
4. Returns an `AgentReply` with the output text and model ID

## Defining Tools

Override `DefineTools()` to expose tools the LLM can call:

```csharp
using Microsoft.Extensions.AI;

protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(SearchKnowledgeBase, "search",
        "Search the knowledge base for relevant information"),
    AIFunctionFactory.Create(CreateReminder, "create_reminder",
        "Create a reminder for a future date")
];

private async Task<string> SearchKnowledgeBase(string query)
{
    return $"Results for: {query}";
}

private async Task<string> CreateReminder(string text, DateTime dueDate)
{
    await SetMemoryAsync($"reminder-{Guid.NewGuid():N}", text);
    return $"Reminder set for {dueDate:g}";
}
```

Tools are discoverable via `InvokeToolAsync`, which finds the matching `AIFunction` by name:

```csharp
var result = await agent.InvokeToolAsync("search", new Dictionary<string, string>
{
    ["query"] = "project status"
});
```

## Memory

The agent's key-value memory is stored in `IDurableDictionary<string, string>`. All mutations are persisted via `WriteStateAsync()`.

### Set and get values

```csharp
await agent.SetMemoryAsync("user-name", "Alice");
var name = await agent.GetMemoryAsync("user-name");
```

## Messages

Conversation messages are stored as a durable list of `AgentMessage` records. Messages are automatically managed by `RespondAsync`, but you can also add entries manually:

```csharp
await agent.AppendMessageAsync(new AgentMessage
{
    Role = "system",
    Content = "Agent initialized at startup"
});

var messages = await agent.QueryMessagesAsync(new AgentMessageQuery
{
    Role = "user",
    Limit = 10,
    Descending = true
});

foreach (var msg in messages)
{
    Console.WriteLine($"[{msg.TimestampUtc:u}] {msg.Role}: {msg.Content}");
}
```

### AgentMessageQuery

Filter messages with these optional parameters:

| Property | Type | Purpose |
|---|---|---|
| `Limit` | `int?` | Maximum number of messages to return |
| `SinceUtc` | `DateTimeOffset?` | Only messages after this timestamp |
| `Role` | `string?` | Filter by role (e.g. `"user"`, `"assistant"`) |
| `Descending` | `bool` | Reverse chronological order |

Each message is also published to the `"agent-history"` Orleans stream for real-time subscribers.

## Events

Record typed events with optional payloads and metadata:

```csharp
await agent.AppendEventAsync(new AgentEvent
{
    Type = "task-completed",
    Payload = "{\"taskId\":\"abc\",\"duration\":42}"
});

var events = await agent.QueryEventsAsync(new AgentEventQuery
{
    Type = "task-completed",
    Limit = 5,
    Descending = true
});
```

### AgentEventQuery

| Property | Type | Purpose |
|---|---|---|
| `Limit` | `int?` | Maximum events to return |
| `SinceUtc` | `DateTimeOffset?` | Only events after this timestamp |
| `Type` | `string?` | Filter by event type |
| `Descending` | `bool` | Reverse chronological order |

Events are persisted durably and published to the `"agent-events"` Orleans stream.

## Scheduling

The scheduling system runs periodic ticks for monitoring or polling tasks. It uses Orleans reminders for intervals >= 1 minute (silo-crash-safe) and grain timers for shorter intervals.

```csharp
// Tick every 5 minutes, stop after 10 ticks
await agent.StartScheduleAsync(TimeSpan.FromMinutes(5), maxTicks: 10);

// Check schedule state
var status = await agent.GetScheduleStatusAsync();
Console.WriteLine($"Running: {status.IsRunning}, Ticks: {status.TickCount}/{status.MaxTicks}");

// Stop early
await agent.StopScheduleAsync();
```

Override `OnScheduleTickAsync` in your agent to handle each tick:

```csharp
protected override async Task OnScheduleTickAsync(int tickCount, CancellationToken ct = default)
{
    await AppendEventAsync(new AgentEvent
    {
        Type = "monitor.tick",
        Payload = $"{{\"tick\":{tickCount}}}"
    }, ct);
}
```

The `ScheduleStatus` record:

| Property | Type | Description |
|---|---|---|
| `IsRunning` | `bool` | Whether the schedule is active |
| `Interval` | `TimeSpan` | Time between ticks |
| `TickCount` | `int` | Ticks completed so far |
| `MaxTicks` | `int?` | Maximum ticks before auto-stop (null = unlimited) |

On grain reactivation, `OnActivateAsync` resumes the schedule if it should still be running.

## Streams

Publish messages to Orleans streams for real-time communication:

```csharp
await agent.PublishStreamAsync("my-namespace", streamId, "Hello from agent!");
```

The agent uses the `"agents"` stream provider configured by `AddIAW()` in the Aspire AppHost.

## Complete Example: Weather Monitor Agent

```csharp
using Core.AI;
using Core.AI.Models;
using Core.V2;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public class WeatherMonitor(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : AgentV2(messages, memory, events, subscriptions, notifications, tracking)
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Weather Monitor",
        Instructions = "You monitor weather conditions and alert subscribers.",
        Capabilities = ["weather", "monitoring"]
    };

    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
        => RespondWithLlmAsync(chatClient, request, ct);

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(GetWeather, "get_weather", "Get current weather for a city")
    ];

    protected override async Task OnScheduleTickAsync(int tickCount, CancellationToken ct = default)
    {
        var city = await GetMemoryAsync("monitored-city", ct) ?? "Seattle";
        var weather = await GetWeather(city);

        await AppendEventAsync(new AgentEvent
        {
            Type = "weather.check",
            Payload = weather
        }, ct);
    }

    private Task<string> GetWeather(string city)
        => Task.FromResult($"{{\"city\":\"{city}\",\"temp\":18,\"condition\":\"cloudy\"}}");
}
```
