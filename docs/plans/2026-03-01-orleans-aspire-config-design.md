# Orleans/Aspire Config Refactoring + MCP Client

**Date**: 2026-03-01
**Approach**: Hybrid — Aspire handles topology, silos keep explicit storage/streaming config

## Problem

Manual Orleans endpoint wiring in AppHost duplicates what Aspire handles automatically:
- Hardcoded `.WithEndpoint("orleans-silo", port: 11111)` / `.WithEndpoint("orleans-gateway", port: 30000)`
- Manual `.WithEnvironment("IAW__Orleans__Gateways__0", ...)` for client discovery
- `ParseEndpoint` / `ResolveEndpoint` DNS helpers duplicated in both silo projects
- `UseLocalhostClustering` with manual `PrimarySiloEndpoint` config for multi-silo
- MCP server is disconnected from Orleans — only has a sample RandomNumberTools

## Design

### 1. AppHost (AppHost.cs)

Remove all manual endpoint/environment configuration. Use Aspire's `.WithReference(orleans)` for silos and `.WithReference(orleans.AsClient())` for clients. Add MCP as an Aspire-managed project.

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<Qwen25>();

var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.AddProject<Projects.DevUI>("devui")
    .WithReference(qwen)
    .WithReference(iaw.AsClient())
    .WaitFor(qwen)
    .WaitFor(samples);

var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient());
```

**Removed**: `.WithEndpoint("orleans-silo/gateway")`, `.WithEnvironment("IAW__Orleans__Gateways__0")`, hardcoded ports.

### 2. Silo Projects (Samples, TelegramBot)

Remove clustering config and DNS helper functions. Keep storage/streaming/reminders in UseOrleans lambda:

```csharp
builder.Host.UseOrleans(silo =>
{
    silo.AddMemoryGrainStorage("Default");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams("agents");
    silo.UseInMemoryReminderService();
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
});
```

**Deleted**: `ParseEndpoint()`, `ResolveEndpoint()`, `IAW:Orleans:*` config reads, `System.Net`/`System.Net.Sockets` usings.

### 3. MCP Server (src/IAW.MCP/)

Convert from standalone NuGet tool to Aspire-managed Orleans client project.

**MCP.csproj**: Remove standalone packaging props (`SelfContained`, `PublishSingleFile`, `PackAsTool`, `PackageType`). Add Core project reference + Orleans client packages.

**Program.cs**: Add Orleans client via `builder.UseOrleansClient()` (Aspire injects clustering config). Register `AgentTools` instead of `RandomNumberTools`.

**Tools/AgentTools.cs**: Two MCP tools:
- `agent_list_all` — lists well-known agents by calling `GetMetadataAsync`
- `assistant_chat` — sends a message to PersonalAssistant via `SendAsync`

Delete `RandomNumberTools.cs`.

### 4. Testing (AspireAgentTest)

May need endpoint name update since Aspire auto-generates Orleans endpoint names when manual `.WithEndpoint()` is removed. Verify during implementation.

## Files Touched

| File | Action |
|------|--------|
| `src/IAW.AppHost/AppHost.cs` | Remove manual endpoints/env vars, add MCP project |
| `samples/Samples/Program.cs` | Remove clustering config + ParseEndpoint helpers |
| `src/Clients.Telegram.Bot/Program.cs` | Remove clustering config + ParseEndpoint helpers |
| `src/IAW.MCP/MCP.csproj` | Add Orleans + Core refs, remove standalone packaging |
| `src/IAW.MCP/Program.cs` | Add Orleans client + MCP tools registration |
| `src/IAW.MCP/Tools/AgentTools.cs` | New — assistant_chat + agent_list_all |
| `src/IAW.MCP/Tools/RandomNumberTools.cs` | Delete |
| `src/IAW.Testing/AspireAgentTest.cs` | Update endpoint discovery if needed |
| `Directory.Packages.props` | Add Orleans client package if not present |
