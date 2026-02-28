# Interactive Agents (IAW)

[![CI](https://github.com/InteractiveAgents/IAW/actions/workflows/ci.yml/badge.svg)](https://github.com/InteractiveAgents/IAW/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-11.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/11.0)

An open-source ecosystem of intelligent agents that collaborate, remember, improve, and orchestrate tasks — powered by Orleans and .NET Aspire.

## Features

- **Durable Memory** — Agent state, history, events, and notifications are durably persisted via Orleans journaled grains. Agents survive restarts without losing context.
- **Agent-to-Agent Communication** — Publish/subscribe notifications and Orleans streams let agents collaborate across a distributed cluster.
- **LLM Integration** — First-class `Microsoft.Extensions.AI` support. Plug in any chat client and agents stream responses with full tool-calling capabilities.
- **Generic Tools** — Define tools as `AIFunction` instances; the runtime discovers, routes, and invokes them automatically.
- **Observability** — Built-in OpenTelemetry metrics and distributed tracing for every send, tool call, and failure.
- **Aspire-Native** — Ships with an Aspire AppHost, service defaults, and dashboard integration for local development and production deployment.

## Quick Start

```bash
dotnet add package IAW.Core
```

Create a minimal agent:

```csharp
using Core;
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

## Documentation

Full documentation is available at **[interactiveagents.github.io/IAW](https://interactiveagents.github.io/IAW)**.

## Building from Source

```bash
git clone https://github.com/InteractiveAgents/IAW.git
cd IAW
dotnet build IAW.slnx
dotnet test IAW.slnx
```

## Project Structure

| Path | Description |
|------|-------------|
| `src/Core` | Agent base class, grain interfaces, models, sessions, and context providers |
| `src/IAW.AppHost` | .NET Aspire AppHost for local orchestration |
| `src/IAW.ServiceDefaults` | Shared Aspire service defaults (telemetry, health checks, resilience) |
| `src/Clients.Telegram.Bot` | Telegram bot client integration |
| `src/IAW.MCP` | MCP server bridge for external orchestration |
| `samples/Samples` | Sample agents and usage examples |
| `test/*` | Unit and integration tests |
| `website/` | Documentation site source (VitePress) |

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
