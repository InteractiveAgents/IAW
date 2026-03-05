# DevUI Orleans Bridge Design

## Problem

DevUI registers a raw Ollama `IChatClient` via `AddOllamaApiClient().AddChatClient()`, bypassing the Orleans agent infrastructure entirely. No GenAI telemetry is emitted because the agent grains (which have `UseOpenTelemetry()` on their `IChatClient`) are never called.

## Solution

Bridge DevUI to Orleans `IAgentV2` grains via an `OrleansAgentChatClient : IChatClient` adapter. DevUI connects as an Orleans client to the samples silo (same pattern as MCP).

## Architecture

```
DevUI (Orleans Client) → IClusterClient → samples silo (Orleans Silo)
  ├── OrleansAgentChatClient : IChatClient
  │     └── IAgentV2.RespondAsync(AgentRequest) → AgentReply
  ├── AddAIAgent() per well-known agent
  ├── MapDevUI() / MapOpenAIResponses() / MapOpenAIConversations()
  └── Connects via Orleans:PrimaryGateway env var
```

## Components

### OrleansAgentChatClient

- Implements `IChatClient`
- Takes `IClusterClient` via DI
- Extracts target agent ID from `ChatOptions.Instructions`
- Calls `cluster.GetGrain<IAgentV2>(agentId).RespondAsync(new AgentRequest { Input = userText })`
- Returns `AgentReply.Output` as chat response
- `GetStreamingResponseAsync` yields a single update (non-streaming, adequate for now)

### Agent Registration

Hardcode well-known agents from the silo:
- personal-assistant, roslyn, dotnet, nuget, github, reviewer
- fs, shell, git, build, knowledge, user, planning, notification

Each registered via `AddAIAgent(name, instructions: agentId)`.

### Orleans Client Setup

Same pattern as MCP project:
- `UseOrleansClient` with `UseStaticClustering` pointing to `Orleans:PrimaryGateway`
- AppHost provides `iaw.AsClient()` + gateway endpoint

### Telemetry

Comes for free through the agent pipeline:
- Agent grains use `UseOpenTelemetry()` on their `IChatClient` (LlmRegistration)
- Agent spans (`agent.respond`, `agent.llm`) emitted by AgentV2
- ServiceDefaults registers `"Core.Agent"` and `"Microsoft.Extensions.AI"` sources
- Bump DevUI sample ratio to 1.0 for development

## Changes

### Remove
- Standalone `/assistant/respond` endpoint
- Direct Ollama `IChatClient` registration (`AddOllamaApiClient`)
- Custom `ActivitySource("DevUI")`
- `CommunityToolkit.Aspire.OllamaSharp` package reference

### Add
- `OrleansAgentChatClient.cs` in DevUI project
- Orleans client configuration in DevUI Program.cs
- Core project reference (for IAgentV2, AgentRequest, AgentReply)

### Modify
- DevUI Program.cs — replace Ollama setup with Orleans client + agent registration
- DevUI.csproj — swap package references
- AppHost.cs — add Orleans gateway endpoint env var for DevUI (like MCP)
- DevUI appsettings.json — sample ratio 1.0
