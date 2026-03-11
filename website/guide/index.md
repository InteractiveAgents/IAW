# Getting Started

Interactive Agents (IAW) is an Orleans-based multi-agent runtime for .NET. Agents are durable grains that communicate through typed messages, Orleans streams, and AI-powered conversation. V3 introduces behavior composition via interfaces, a typed message hierarchy, and stream-based event pipelines.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) (for the AppHost)

## Installation

Add the core package to your project:

```bash
dotnet add package IAW.Core
```

## Creating Your First Agent

Every agent extends the `Agent` base class from `Core.V3`. Override `Instructions` and `DisplayName`:

```csharp
using Core.V3;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface IGreeterAgent : IAgent;

public class GreeterAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IGreeterAgent
{
    protected override string Instructions => "You are a friendly greeter.";
    protected override string DisplayName => "Greeter";
}
```

The constructor takes five durable state parameters that Orleans manages automatically. You never create these yourself -- Orleans injects them when the grain activates.

## Aspire Integration

IAW runs inside a .NET Aspire AppHost. The `AddIAW` extension configures Orleans with development clustering, in-memory grain storage, streaming, and reminders:

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>();

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```

`AddIAW` configures:
- Development clustering (cluster ID `"dev"`, service ID `"dev"`)
- In-memory grain storage for `Default` and `PubSubStore`
- Memory streaming provider named `"agents"`
- Memory-based reminders
- State machine storage for durable collections

`WithLLM<TModel>()` registers an LLM model. `WithLLMEnvironment()` injects environment variables for model IDs, provider types, and API keys into the project.

## Running

Start the project with the Aspire CLI:

```bash
aspire run
```

This starts the AppHost, all orchestrated resources, the Aspire dashboard, and any container dependencies.

## Interacting with Agents

Once an agent grain is activated, interact with it through the `IAgent` grain interface:

```csharp
var agent = grainFactory.GetGrain<IAgent>("greeter");

// Conversation
var response = await agent.GetResponse("Hello!", ct);
var history = await agent.GetHistory(ct);

// Streaming response
await foreach (var chunk in agent.GetResponseStream("Tell me a story", ct))
{
    Console.Write(chunk);
}

// State
await agent.SetWorkspaceAsync("/path/to/project", ct);
var state = await agent.GetStateAsync(ct);

// Metadata
var metadata = await agent.GetMetadataAsync(ct);
var capabilities = await agent.GetCapabilitiesAsync(ct);

// Events
var eventLog = await agent.GetEventLogAsync(ct);

// Streams
var subscriptions = await agent.GetActiveSubscriptionsAsync(ct);
```

## LLM Integration

The `Agent` base class takes an `IChatClient` (from `Microsoft.Extensions.AI`) in its constructor. On activation, it creates an `AIAgent` from the Microsoft Agent Framework with durable chat history:

```csharp
using Core.V3;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface IAssistantAgent : IAgent;

public class AssistantAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IAssistantAgent
{
    protected override string Instructions =>
        "You are a helpful personal assistant. Be concise and accurate.";
}
```

`GetResponse` and `GetResponseStream` handle the full flow: building chat history from durable storage, including tools from `DefineTools()`, calling the LLM, and persisting the conversation.

## Next Steps

- [Architecture](/guide/architecture) -- understand the V3 class hierarchy, behavior composition, and stream patterns
- [Building Agents](/guide/agents) -- constructor params, custom tools, override points
- [Events & Streams](/guide/events-streams) -- typed events, stream composition, pipeline patterns
- [Message Types](/guide/messages) -- ICommand, IEvent, INotification
- [Communication](/guide/communication) -- pub/sub, P2P, broadcast patterns
- [LLM Agents](/guide/llm-agents) -- model hierarchy, [Llm&lt;T&gt;] attribute, adding new models
- [Persistence](/guide/persistence) -- CosmosDB, Qdrant, in-memory vs durable mode
- [Orchestration](/guide/orchestration) -- PlanningAgent, ScriptGenerator, ScriptExecutor
- [Consilium](/guide/consilium) -- multi-model patterns: routing, voting, synthesis
- [Memory](/guide/memory) -- memory agents, MemoryEntry, context providers
- [Task Supervision](/guide/supervisor) -- stall detection, escalation, notification routing
- [MCP Server](/guide/mcp) -- orchestrate agents from Claude Code via MCP
