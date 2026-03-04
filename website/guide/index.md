# Getting Started

Interactive Agents (IAW) is an Orleans 10.0-based multi-agent runtime for .NET 11. Agents are durable, observable, LLM-powered grains that communicate through pub/sub notifications and Orleans streams. V2 introduces `AgentV2` -- a single flat base class that hides all durable state plumbing behind clean overrides.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) (for the AppHost)

## Installation

Add the core package to your project:

```bash
dotnet add package IAW.Core
```

## Creating Your First Agent

Every agent extends the `AgentV2` base class. The only required override is `Profile`:

```csharp
using Core.V2;

public class Greeter : AgentV2
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Greeter",
        Instructions = "You are a friendly greeter."
    };
}
```

This gives you a fully functional agent with durable messages, memory, events, notifications, scheduling, tools, and streaming -- all inherited from `AgentV2`.

## AgentProfile

The `Profile` property returns an `AgentProfile` that identifies the agent:

| Property | Type | Purpose |
|---|---|---|
| `Id` | `string` | Unique agent identifier (typically the grain primary key) |
| `DisplayName` | `string` | Human-readable name |
| `Description` | `string?` | Optional description |
| `Instructions` | `string` | LLM system prompt |
| `Capabilities` | `List<string>` | Advertised capabilities |

## Aspire Integration

IAW runs inside a .NET Aspire AppHost. The `AddIAW` extension configures Orleans with development clustering, in-memory grain storage, streaming, and reminders:

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>();

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```

`AddIAW` returns an `OrleansService` and configures:
- Development clustering (cluster ID `"dev"`, service ID `"dev"`)
- In-memory grain storage for `Default` and `PubSubStore`
- Memory streaming provider named `"agents"`
- Memory-based reminders

`WithLLM<TModel>()` registers an LLM model. `WithLLMEnvironment()` injects environment variables for model IDs, provider types, and API keys (as Aspire secret parameters) into the project resource.

## Running

Always use the Aspire CLI to start the project:

```bash
aspire run
```

This starts the AppHost and all orchestrated resources including silos, the Aspire dashboard, and any container dependencies.

## Interacting with Agents

Once an agent grain is activated, interact with it through the `IAgentV2` grain interface:

```csharp
var agent = grainFactory.GetGrain<IAgentV2>("greeter");

// Read profile
var profile = await agent.GetProfileAsync();

// Send a request and get a reply
var reply = await agent.RespondAsync(new AgentRequest { Input = "Hello!" });

// Store and retrieve memory
await agent.SetMemoryAsync("mood", "happy");
var mood = await agent.GetMemoryAsync("mood");

// Query conversation messages
var messages = await agent.QueryMessagesAsync(new AgentMessageQuery { Limit = 10 });

// Append an event
await agent.AppendEventAsync(new AgentEvent { Type = "greeted", Payload = "{\"user\":\"Alice\"}" });

// Subscribe to notifications
await agent.SubscribeAsync("updates", "other-agent-id");

// Send a notification
await agent.NotifyAsync(new NotificationEnvelope { Topic = "updates", Payload = "{\"message\":\"hello\"}" });

// Start periodic scheduling (interval, maxTicks)
await agent.StartScheduleAsync(TimeSpan.FromMinutes(5), maxTicks: 10);

// Invoke a tool by name
var result = await agent.InvokeToolAsync("search", new Dictionary<string, string>
{
    ["query"] = "latest news"
});
```

## LLM Integration

The `AgentV2` base class provides a `RespondWithLlmAsync` helper for derived agents to call an LLM. Inject an `IChatClient` via the `[Llm<TModel>]` attribute and use it in your `OnRespondAsync` override:

```csharp
using Core.AI;
using Core.AI.Models;
using Core.V2;
using Microsoft.Extensions.AI;

public class Assistant(
    // ... durable state params inherited from AgentV2 ...
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : AgentV2
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Assistant",
        Instructions = "You are a helpful assistant."
    };

    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
        => RespondWithLlmAsync(chatClient, request, ct);
}
```

`RespondWithLlmAsync` builds the full chat history (including system prompt from `Profile.Instructions`), calls `IChatClient.GetResponseAsync`, and returns an `AgentReply` with the output text and model ID.

## Next Steps

- [Architecture](/guide/architecture) -- understand the AgentV2 class hierarchy and durable state model
- [Building Agents](/guide/agents) -- override Profile, OnRespondAsync, DefineTools, and OnScheduleTickAsync
- [Notifications & Events](/guide/notifications) -- pub/sub communication between agents
- [MCP Server](/guide/mcp) -- orchestrate agents from Claude Code via MCP
