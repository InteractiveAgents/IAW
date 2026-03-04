# Interactive Agents (IAW)

[![CI](https://github.com/InteractiveAgents/IAW/actions/workflows/ci.yml/badge.svg)](https://github.com/InteractiveAgents/IAW/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-11.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/11.0)

IAW is an Orleans-based multi-agent runtime for .NET. Build intelligent agents that collaborate, remember, and improve.

## Quick Start

```bash
dotnet add package IAW.Core
```

Create a minimal agent:

```csharp
using Core.V2;

public class GreeterAgent : AgentV2
{
    protected override AgentProfile Profile => new()
    {
        DisplayName = "Greeter",
        Instructions = "You are a friendly greeter.",
        Capabilities = ["chat"]
    };

    protected override async Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
    {
        return new AgentReply { Output = $"Hello, {request.Input}!" };
    }
}
```

## Features

- **Durable state** -- Agent messages, memory, events, and notifications survive restarts via Orleans journaled grains
- **Agent-to-agent communication** -- Pub/sub notifications and Orleans streams for distributed collaboration
- **LLM integration** -- First-class `Microsoft.Extensions.AI` support with streaming and tool calling
- **Generic tools** -- Define tools as `AITool` instances; the runtime discovers and invokes them automatically
- **Scheduling** -- Built-in timer/reminder support for periodic agent tasks
- **Observability** -- OpenTelemetry metrics and distributed tracing for every operation
- **Aspire-native** -- Ships with AppHost, service defaults, and dashboard integration
- **MCP orchestration** -- External orchestrators (Claude Code, etc.) control agents via MCP tools

## Architecture

Agents are Orleans grains extending `AgentV2` with durable journaled state. Each agent exposes a flat `IAgentV2` interface covering profile, respond, messages, memory, events, notifications, scheduling, streaming, and tools. .NET Aspire orchestrates the runtime, LLM providers, and observability.

## Documentation

Full documentation is available at **[interactiveagents.github.io/IAW](https://interactiveagents.github.io/IAW)**.

## NuGet Packages

| Package | Purpose |
|---------|---------|
| `IAW.Core` | Agent base class (`AgentV2`), contracts, LLM integration |
| `IAW.Hosting` | Aspire extensions: `AddIAW()`, `WithLLM()` |
| `IAW.Agents` | Built-in agents (FileSystem, Shell, Git, PersonalAssistant, etc.) |
| `IAW.Agents.CSharp` | Roslyn-powered C# agents |
| `IAW.Testing` | `AgentTestV2<T>` + universal behavior tests |
| `IAW.Clients` | Orleans client bootstrap |
| `IAW.MCP` | MCP server bridge |

## Project Structure

| Path | Description |
|------|-------------|
| `src/Core` | Agent base class, grain interfaces, LLM integration, V2 contracts |
| `src/IAW.AppHost` | .NET Aspire AppHost for local orchestration |
| `src/IAW.ServiceDefaults` | Shared Aspire service defaults (telemetry, health checks) |
| `src/Clients.Telegram.Bot` | Telegram bot client integration |
| `src/IAW.MCP` | MCP server bridge for external orchestration |
| `src/DevUI` | Microsoft Agent Framework dev UI |
| `samples/Samples` | Sample agents and HTTP endpoints |
| `samples/IAW.Testing` | Testing framework |
| `test/Core.Tests` | Unit tests + architecture guards |
| `test/Integration.Tests` | Aspire integration tests |

## Build and Run

```bash
git clone https://github.com/InteractiveAgents/IAW.git
cd IAW
dotnet build IAW.slnx
dotnet test IAW.slnx
aspire run
```

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
