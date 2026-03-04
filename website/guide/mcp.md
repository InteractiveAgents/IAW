# MCP Server

IAW includes a [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that lets AI assistants like Claude Code orchestrate agents in the running Orleans cluster. The MCP server connects as an Orleans client and exposes agent operations as tools.

## What is MCP

The Model Context Protocol is an open standard for connecting AI assistants to external tools and data sources. IAW's MCP server bridges Claude Code (or any MCP-compatible client) to the agent runtime, enabling AI-assisted orchestration of the engineering team.

## Architecture

```
Claude Code  <--MCP/HTTP-->  IAW.MCP  <--Orleans Client-->  Orleans Cluster
```

The MCP server (`src/IAW.MCP`) is a standalone ASP.NET project that:
1. Connects to the Orleans cluster as a client (via `UseOrleansClient`)
2. Registers MCP tools using `ModelContextProtocol.Server`
3. Exposes an HTTP transport endpoint via `MapMcp()`

## Available Tools

| Tool | Description |
|---|---|
| `agent_list_all` | List all registered agents with their profile and capabilities |
| `assistant_chat` | Send a message to the PersonalAssistant and get a response |
| `agent_send_message` | Send a message to any agent by ID and get a response |
| `agent_get_status` | Get an agent's profile and recent activity |
| `agent_assign_task` | Assign a task to PersonalAssistant for delegation to the team |
| `agent_get_events` | Get events from an agent's event log |
| `agent_get_metrics` | Get agent performance metrics (message count, event count, schedule status) |
| `agent_trigger_self_improvement` | Trigger self-improvement analysis across the agent team |

## Well-Known Agent IDs

These agents are discoverable via `agent_list_all`:

### Engineering Team

| Agent | ID | Role |
|---|---|---|
| PersonalAssistant | `personal-assistant` | CEO -- decomposes tasks, delegates |
| Roslyn | `roslyn` | C# code intelligence (syntax trees, types) |
| DotNet | `dotnet` | Build, test, format |
| NuGet | `nuget` | Package management |
| GitHub | `github` | PRs, issues, releases |
| Reviewer | `reviewer` | Code quality review |
| SelfImprovement | `self-improvement` | Analyze and improve agent code |

### Infrastructure Agents

| Agent | ID | Role |
|---|---|---|
| FileSystem | `fs` | Read/write/search files |
| Shell | `shell` | Shell command execution |
| Git | `git` | Version control |
| Build | `build` | Generic build runner |
| Knowledge | `knowledge` | Project knowledge store |
| User | `user` | Preferences, memories |
| Planning | `planning` | Execution plan generation |
| Notification | `notification` | User alerts |

## AppHost Configuration

The MCP server is configured in the AppHost:

```csharp
builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", samples.GetEndpoint("orleans-gateway"))
    .WaitFor(samples);
```

It connects as an Orleans client (`.AsClient()`) and waits for the samples silo to be running before accepting connections.

## Claude Code Configuration

Add the MCP server to your Claude Code configuration (`.claude/mcp.json` or `~/.claude/mcp.json`):

```json
{
  "mcpServers": {
    "iaw": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

The URL should match the endpoint where the MCP project is running. When using Aspire, check the dashboard for the actual port.

## Example Usage

### Chat with the assistant

```
> assistant_chat("Analyze the current project structure and suggest improvements")
```

### Send a message to a specific agent

```
> agent_send_message("roslyn", "List all public interfaces in the Core project")
```

### Assign a task

```
> agent_assign_task("Add input validation to the WeatherMonitor agent", priority: "high")
```

### Check agent status

```
> agent_get_status("personal-assistant")
```

### View recent events

```
> agent_get_events("roslyn", limit: 10)
```

## Tool Implementation

Tools are implemented in `AgentTools.cs` using the `[McpServerTool]` attribute:

```csharp
[McpServerTool(Name = "assistant_chat")]
[Description("Send a message to the PersonalAssistant and get a response.")]
public async Task<string> AssistantChat(
    [Description("The message to send to the assistant")] string message,
    CancellationToken ct)
{
    var assistant = orleans.GetGrain<IAgent>("personal-assistant");
    var request = new AgentRequest { Input = message };
    var reply = await assistant.RespondAsync(request, ct);
    return JsonSerializer.Serialize(new { reply.Output, reply.ModelId, reply.TimestampUtc }, JsonOptions);
}
```

All tools use the V2 `AgentRequest`/`AgentReply` contracts and return JSON-serialized responses.
