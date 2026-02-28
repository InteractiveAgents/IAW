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

features:
  - icon: "\U0001F9E0"
    title: Durable Memory
    details: Every agent has six built-in durable state collections backed by Orleans journaled grain storage — key-value pairs, conversation history, events, subscriptions, notifications, and tracking status.
  - icon: "\U0001F4E1"
    title: Agent-to-Agent Communication
    details: Agents communicate through pub/sub notifications, Orleans streams, and direct grain calls. Subscribe to topics, broadcast events, and build reactive multi-agent workflows.
  - icon: "\U0001F916"
    title: LLM Integration
    details: Plug in any LLM provider — Anthropic, OpenAI, or Ollama — through Microsoft.Extensions.AI. Agents stream responses via SendAsync and register custom tools through DefineTools.
  - icon: "\U0001F527"
    title: Generic Tools
    details: Define agent-specific tools as AIFunction instances. The base Agent class discovers and invokes them automatically through InvokeToolAsync with full observability tracing.
  - icon: "\U0001F4CA"
    title: Observability
    details: Built-in OpenTelemetry tracing and metrics via System.Diagnostics. Track sends, tool calls, and failures with the Core.Agent ActivitySource and Meter.
  - icon: "\U0001F680"
    title: Aspire-Native
    details: First-class .NET Aspire integration. AddIAW() configures the full Orleans cluster, WithLLM() declares models, and WithLLMEnvironment() wires API keys — all in the AppHost.
---

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
