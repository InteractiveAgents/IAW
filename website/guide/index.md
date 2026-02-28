# Getting Started

Interactive Agents (IAW) is an Orleans 10.0-based multi-agent runtime for .NET 11. Agents are durable, observable, LLM-powered grains that communicate through pub/sub notifications and Orleans streams.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) (for the AppHost)

## Installation

Add the core package to your project:

```bash
dotnet add package IAW.Core
```

## Creating Your First Agent

Every agent extends the `Agent` base class, which requires six durable state collections injected through primary constructor parameters. Each parameter is annotated with the `[Memory]` attribute to bind it to a named Orleans journaled storage key.

```csharp
using Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public class GreeterAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "Greeter";
    public override string SystemPrompt => "You are a friendly greeter.";
}
```

The `[Memory]` attribute is a thin wrapper over `[FromKeyedServices]`:

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MemoryAttribute(string name) : FromKeyedServicesAttribute(name);
```

## Virtual Properties

Override these properties on your agent to customize its identity:

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Id` | `string` | Grain primary key | Unique agent identifier |
| `DisplayName` | `string` | Same as `Id` | Human-readable name |
| `SystemPrompt` | `string` | `string.Empty` | LLM system prompt |

## Aspire Integration

IAW is designed to run inside a .NET Aspire AppHost. The `AddIAW` extension configures Orleans with development clustering, in-memory grain storage, streaming, and reminders.

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>()
    .WithLLM<Claude45Haiku>();

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```

`AddIAW` returns an `OrleansService` and configures:
- Development clustering
- In-memory grain storage for `Default` and `PubSubStore`
- Memory streaming provider named `"agents"`
- Memory-based reminders

`WithLLM<TModel>()` registers an LLM model. `WithLLMEnvironment()` injects environment variables for model IDs, provider types, and API keys (as Aspire secret parameters) into the project resource.

## Interacting with Agents

Once an agent grain is activated, interact with it through the `IAgent` grain interface:

```csharp
var agent = grainFactory.GetGrain<IAgent>("greeter");

// Read metadata
var metadata = await agent.GetMetadataAsync();

// Store and retrieve state
await agent.SetStateAsync("mood", "happy");
var mood = await agent.GetStateValueAsync("mood");

// Increment a counter
var count = await agent.IncrementAsync("greetings");

// Get conversation history
var history = await agent.GetHistoryAsync();

// Publish an event
await agent.PublishEventAsync("greeted", "{\"user\":\"Alice\"}");

// Subscribe to notifications
await agent.SubscribeAsync("updates", "other-agent-id");

// Send a notification
await agent.NotifyAsync("updates", "{\"message\":\"hello\"}");

// Start periodic tracking (interval, maxTicks)
await agent.StartTrackingAsync(TimeSpan.FromMinutes(5), maxTicks: 10);

// Invoke a tool by name
var result = await agent.InvokeToolAsync("search", new Dictionary<string, string>
{
    ["query"] = "latest news"
});
```

## LLM Streaming

The `SendAsync` method is available on the `Agent` class directly (not on the `IAgent` grain interface). It streams LLM responses as an `IAsyncEnumerable<string>`:

```csharp
await foreach (var token in agent.SendAsync("Hello, who are you?"))
{
    Console.Write(token);
}
```

`SendAsync` automatically records user and assistant messages to durable history, tracks send/failure metrics through `AgentObservability`, and emits OpenTelemetry activities under the `Core.Agent` ActivitySource.

## Next Steps

- [Architecture](/guide/architecture) -- understand the agent class hierarchy and durable state model
- [Building Agents](/guide/agents) -- add LLM support, define tools, manage state
- [Notifications & Events](/guide/notifications) -- pub/sub communication between agents
