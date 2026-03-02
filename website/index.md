---
layout: home

hero:
  name: Interactive Agents
  text: Build intelligent agent systems on .NET
  tagline: An open-source ecosystem of agents that collaborate, remember, improve, and orchestrate tasks — powered by Orleans and Aspire.
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

<BehaviorTabs />

## Quick Start

### Install the package

```bash
dotnet add package IAW.Core
```

### Create your first agent

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

### Configure with Aspire

```csharp
using Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>();

builder.AddProject<Projects.MySilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```
