---
layout: home

hero:
  name: Interactive Agents
  text: Build intelligent agent systems on .NET
  tagline: An open-source multi-agent runtime powered by Orleans and Aspire -- compose behaviors via typed interfaces, stream events between agents, and let AI handle the rest.
  image:
    src: /logo.svg
    alt: Interactive Agents
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: View on GitHub
      link: https://github.com/InteractiveAgents/IAW

---

<ArchitectureDiagram />

## Quick Start

### Install the package

```bash
dotnet add package IAW.Core
```

### Create your first agent

```csharp
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

public interface IGreeterAgent : IAgent;

public class GreeterAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IGreeterAgent
{
    protected override string Instructions => "You are a friendly greeter.";
    protected override string DisplayName => "Greeter";
}
```

### Configure with Aspire

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>();

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(iaw);

builder.Build().Run();
```

## Key Features

### Typed Message System

Three message categories with compile-time safety: `ICommand` for directed requests, `IEvent` for broadcast streams, `INotification` for observer-pattern delivery.

### Stream-Based Event Pipelines

Agents auto-subscribe to streams by implementing `IStreamConsumer<TEvent>`. Build pipelines where `CodeChangedEvent` triggers a build agent, which publishes `BuildCompletedEvent` to a deploy agent.

### Compose Behaviors via Interfaces

No deep inheritance. Mix and match `IStreamConsumer<T>`, `IStreamProducer<T>`, `IBroadcaster<T>`, `IReceiver<T>`, and `INotifier<T>` to define exactly how your agent communicates.

### Built-in Tools

Every agent gets `FileTools`, `ShellTools`, `WebTools`, and `WorkspaceTools` out of the box. Add custom tools by overriding `DefineTools()`.

### Auto-Discovery Registry

`AgentRegistrationStartupTask` scans all assemblies on startup and registers every agent in the `AgentRegistryGrain` with its capabilities, published events, and subscriptions.

### LLM-Powered Tracking

Agents can schedule recurring LLM-powered checks with `StartTrackingAsync`. When a tracked item changes, the agent publishes a `tracking.changed` event automatically.
