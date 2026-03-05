# IAW DevUI

Web-based chat UI for interacting with IAW Orleans agents, built on [Microsoft Agent Framework DevUI](https://aka.ms/dotnet/agent-framework/docs).

## Architecture

DevUI runs as an **Orleans client** (not a silo), connecting to the `samples` silo via gateway port 30000.

```
DevUI (Orleans Client)
  └── OrleansAgentChatClient : IChatClient
        └── IClusterClient.GetGrain<IAgent>(agentId).RespondAsync()
              └── Agent grain in samples silo (with UseOpenTelemetry pipeline)
```

`OrleansAgentChatClient` bridges `Microsoft.Extensions.AI.IChatClient` to Orleans `IAgent` grains. The `AddAIAgent()` registration passes the grain ID as `instructions`, which the client uses for routing.

## Running

Always start via Aspire (never `dotnet run` directly):

```bash
aspire run
```

DevUI is available at the `/devui/` path shown in the Aspire dashboard.

## Registered Agents

Well-known agents registered in `Program.cs`:

| Agent | Grain ID | Role |
|-------|----------|------|
| PersonalAssistant | personal-assistant | Task decomposition and delegation |
| Roslyn | roslyn | C# code intelligence |
| DotNet | dotnet | Build, test, format |
| NuGet | nuget | Package management |
| GitHub | github | PRs, issues, releases |
| Reviewer | reviewer | Code quality review |
| FileSystem | fs | File read/write/search |
| Shell | shell | Shell command execution |
| Git | git | Version control |
| Build | build | Generic build runner |
| Knowledge | knowledge | Project knowledge store |
| User | user | Preferences, memories |
| Planning | planning | Execution plan generation |
| Notification | notification | User alerts |

## Telemetry

GenAI telemetry flows through the agent pipeline:
- `agent.respond` spans from `AgentV2.RespondAsync()`
- `agent.llm` + GenAI spans when agents call `RespondWithLlmAsync()`
- Trace sample ratio set to 1.0 for full capture in development

## Key Files

- `Program.cs` -- Orleans client setup, agent registration, DevUI mapping
- `OrleansAgentChatClient.cs` -- IChatClient → IAgent grain bridge
- `appsettings.json` -- Telemetry config (SampleRatio: 1.0)
