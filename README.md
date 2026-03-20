# IAW — Interactive Agents Web

IAW is a customizable system of specialized AI agents that work together as your personal assistant. You talk to one — it orchestrates the rest. A code agent writes, a reviewer checks, a memory agent remembers, a build agent compiles. They share context, coordinate in real-time, and adapt to how you work.

## Why IAW

Most AI agent frameworks today are black boxes. Something happens, you get a result, but you have no idea *why* it chose that path, which model ran, how long it took, or what it actually did. When things go wrong — and they do — you're left guessing. That's not how trust works.

IAW is built around three principles: **transparency**, **collaboration**, and **control**.

**Observable, not opaque.** Every agent call, every LLM request, every tool invocation shows up as a distributed trace in the Aspire dashboard — with model name, token counts, latency, and the full decision chain. You don't have to trust a black box. You can see exactly what happened, why, and how much it cost.

**Agents that specialize and talk to each other.** Instead of one model doing everything, IAW runs a team of focused agents — each good at one thing — that coordinate through clear communication channels: direct calls, typed messages, pub/sub streams, and notifications. A coding task flows through agents that understand your codebase, check packages, run builds, review the output, and remember what worked last time. Every interaction between agents is traceable.

**Memory that persists.** Agents remember your projects, preferences, and past decisions across sessions. They build context over time — not just within a conversation, but across all of them.

**You pick the models.** Mix and match providers freely. Run Claude for orchestration, GPT-5.4 Nano for fast subtasks, Qwen locally for privacy. Compare models side-by-side on the same prompt — see who's faster, cheaper, better. No vendor lock-in, no walled gardens — just operate freely.

**Self-improving.** Agents review each other's work. A reviewer agent checks code changes, a self-improvement agent learns from outcomes. The system gets better the more you use it.

**Not just cloud, not just local.** Use cloud APIs, local Ollama models, or both. Your infrastructure, your rules.

## How It Works

You send a message from Telegram, Claude Code (MCP), or the Web UI. Here's what happens:

```
 You
  │
  ├── Telegram Bot        ─┐
  ├── MCP Server (Claude)  ├──▶  Project Agent
  └── Web UI (DevUI)      ─┘         │
                                      │  understands your request
                                      ▼
                              ┌──────────────┐
                              │  Need tools? │
                              └──────┬───────┘
                                     │
                     ┌───────────────┼───────────────┐
                     ▼               ▼               ▼
               Answer directly   Use a tool    Execute complex task
               (from LLM +      (schedule,     (code orchestration)
                memory)          recall, etc.)        │
                                                      ▼
                                              Code Orchestrator
                                                      │
                                          generates a small C# app
                                          that calls agents directly
                                                      │
                                                      ▼
                                    ┌─────────────────────────────┐
                                    │   Agent Cluster (Orleans)   │
                                    │                             │
                                    │  ┌─────┐ ┌──────┐ ┌─────┐  │
                                    │  │Shell│ │Roslyn│ │ Git │  │
                                    │  └─────┘ └──────┘ └─────┘  │
                                    │  ┌─────┐ ┌──────┐ ┌─────┐  │
                                    │  │Build│ │NuGet │ │FS   │  │
                                    │  └─────┘ └──────┘ └─────┘  │
                                    │  ┌────────┐ ┌───────────┐  │
                                    │  │Memory  │ │ Reviewer  │  │
                                    │  └────────┘ └───────────┘  │
                                    │         ... 65+ more       │
                                    └─────────────────────────────┘
                                                      │
                                                      ▼
                                                  result.json
                                                      │
                                                      ▼
                                              Response back to you
```

**The key idea:** when a task is too complex for a single LLM call, the Project Agent writes a small C# program on the fly. That program connects to the agent cluster and calls specialized agents directly — Shell runs commands, Roslyn analyzes code, Git manages commits, Memory recalls past context. The program runs, produces a result, and the response flows back to you. All of this is observable in the Aspire dashboard as distributed traces.

## Capabilities

- **Multi-agent orchestration** — 65+ agents that communicate via direct calls, typed messages, and pub/sub streams
- **Code orchestration** — agents generate and run C# programs that call other agents for complex workflows
- **Scheduled jobs** — recurring tasks with automatic execution and delivery
- **Durable state** — agent memory, conversation history, and project state survive restarts
- **Telegram bot** — conversational interface with topic-based project organization
- **MCP server** — plug into Claude Code or any MCP client
- **Web UI** — Blazor-based DevUI for direct agent interaction
- **Full observability** — OpenTelemetry traces with GenAI semantic conventions in Aspire dashboard

## Supported Models

| Provider | Models |
|----------|--------|
| OpenAI | GPT-5.4 Mini, GPT-5.4 Nano, GPT-4o |
| Anthropic | Claude Sonnet 4.6, Claude Opus 4.6, Claude 4.5 Haiku |
| Ollama | Qwen 2.5, Llama 3.2 (any Ollama model) |
| GitHub Models | GPT-4o Mini |

Adding a model is one class and one line in the AppHost. Bring your own.

## Prerequisites

```bash
winget install Microsoft.DotNet.SDK.Preview
winget install Microsoft.FoundryLocal
```

## Getting Started

```bash
git clone https://github.com/InteractiveAgents/IAW.git
cd IAW
dotnet build IAW.slnx
dotnet run --project src/IAW.AppHost/Aspire.csproj
```

Aspire prompts for API keys on first run with direct links to where to get them.

## Project Structure

```
src/
  Core/                    Agent base class, contracts, LLM integration
  Agents/                  65+ agent implementations
  Agents.CSharp/           Roslyn, DotNet, GitHub, NuGet agents
  Aspire.Hosting.IAW/      AppHost extensions (AddIAW, WithLLM<T>)
  Aspire.IAW.Client/       Service/client registration, OTel
  IAW.AppHost/             Aspire orchestration
  IAW.Assistant/           Production silo
  IAW.MCP/                 MCP server bridge
  DevUI/                   Blazor web UI
  Clients.Telegram/        Telegram bot
  IAW.Testing/             Test framework
test/
  Core.Tests/              Agent behavior tests
  Integration.Tests/       Aspire integration tests
```

## Creators

- [LeftTwixWand](https://github.com/LeftTwixWand)
- [anton-kharchenko](https://github.com/anton-kharchenko)
- [EaGLenok](https://github.com/EaGLenok)
- [alexbardjo](https://github.com/alexbardjo)

## Contributors

- [ScientistFromMars](https://github.com/ScientistFromMars)

## License

See [LICENSE](LICENSE) for details.
