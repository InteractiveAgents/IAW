# Orleans/Aspire Config Refactoring + MCP Client — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace manual Orleans endpoint/clustering wiring with Aspire's built-in Orleans integration, and wire the MCP server as an Orleans cluster client with `assistant_chat` and `agent_list_all` tools.

**Architecture:** Aspire's `AddOrleans()` + `.WithReference(orleans)` handles all clustering and endpoint discovery automatically. Silo projects keep explicit storage/streaming config in their `UseOrleans` lambda as a safety net. MCP becomes an Aspire-managed project using `.WithReference(orleans.AsClient())` with `UseOrleansClient()` for grain access.

**Tech Stack:** .NET Aspire 13.1.2, Orleans 10.0.1, ModelContextProtocol 1.0.0

---

### Task 1: Clean up Samples/Program.cs — remove manual clustering

**Files:**
- Modify: `samples/Samples/Program.cs:1-28` (Orleans config block) and `:576-613` (ParseEndpoint/ResolveEndpoint helpers)

**Step 1: Remove clustering config and DNS helpers**

Replace the entire `builder.Host.UseOrleans(silo => { ... })` block (lines 11-28) with:

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

Delete the `ParseEndpoint` method (lines 576-595) and the `ResolveEndpoint` method (lines 597-611).

**Step 2: Remove unused usings**

Remove these usings from the top of the file (lines 5-6):
```csharp
// DELETE these:
using System.Net;
using System.Net.Sockets;
```

**Step 3: Build to verify**

Run: `dotnet build samples/Samples/Samples.csproj`
Expected: BUILD SUCCEEDED (no compile errors)

**Step 4: Commit**

```bash
git add samples/Samples/Program.cs
git commit -m "refactor: remove manual Orleans clustering from Samples — Aspire handles it"
```

---

### Task 2: Clean up TelegramBot/Program.cs — remove manual clustering

**Files:**
- Modify: `src/Clients.Telegram.Bot/Program.cs:1-30` (Orleans config block) and `:96-124` (ParseEndpoint/ResolveEndpoint helpers)

**Step 1: Remove clustering config and DNS helpers**

Replace the entire `builder.Host.UseOrleans(silo => { ... })` block (lines 13-30) with:

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

Delete the `ParseEndpoint` method (lines 96-109) and the `ResolveEndpoint` method (lines 111-124).

**Step 2: Remove unused usings**

Remove these usings from the top of the file (lines 5-6):
```csharp
// DELETE these:
using System.Net;
using System.Net.Sockets;
```

**Step 3: Build to verify**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: BUILD SUCCEEDED

**Step 4: Commit**

```bash
git add src/Clients.Telegram.Bot/Program.cs
git commit -m "refactor: remove manual Orleans clustering from TelegramBot — Aspire handles it"
```

---

### Task 3: Clean up AppHost — remove manual endpoints, add TelegramBot Orleans reference

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs` (lines 10-32, 38-41)

**Step 1: Simplify samples resource**

Replace lines 10-22:
```csharp
var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-silo", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 11_111;
    })
    .WithEndpoint("orleans-gateway", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 30_000;
    });
```

With:
```csharp
var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);
```

**Step 2: Simplify DevUI resource**

Replace lines 27-32:
```csharp
builder.AddProject<Projects.DevUI>("devui")
    .WithReference(qwen)
    .WithReference(iaw.AsClient())
    .WaitFor(qwen)
    .WaitFor(samples)
    .WithEnvironment("IAW__Orleans__Gateways__0", samples.GetEndpoint("orleans-gateway"));
```

With:
```csharp
builder.AddProject<Projects.DevUI>("devui")
    .WithReference(qwen)
    .WithReference(iaw.AsClient())
    .WaitFor(qwen)
    .WaitFor(samples);
```

**Step 3: Add Orleans reference to TelegramBot**

Replace lines 38-40:
```csharp
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));
```

With:
```csharp
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));
```

**Step 4: Build to verify**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: BUILD SUCCEEDED

**Step 5: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "refactor: remove manual Orleans endpoints from AppHost — use Aspire WithReference"
```

---

### Task 4: Convert MCP to Aspire-managed Orleans client

**Files:**
- Modify: `src/IAW.MCP/MCP.csproj`
- Modify: `src/IAW.AppHost/Aspire.csproj` (add MCP project reference)
- Modify: `src/IAW.AppHost/AppHost.cs` (add MCP resource)

**Step 1: Update MCP.csproj — remove standalone packaging, add Orleans + Core**

Replace entire `src/IAW.MCP/MCP.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Streaming" />
    <PackageReference Include="ModelContextProtocol" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
  </ItemGroup>

</Project>
```

Removed: `SelfContained`, `PublishSingleFile`, `PackAsTool`, `PackageType`, `PackageId`, `PackageVersion`, `PackageTags`, `Description`, `RuntimeIdentifiers`, `PublishSelfContained`, `PackageReadmeFile`, and the extra `None Include` items for `.mcp/server.json` and `README.md`.

**Step 2: Add MCP project reference to AppHost csproj**

In `src/IAW.AppHost/Aspire.csproj`, add to the `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\IAW.MCP\MCP.csproj" />
```

**Step 3: Add MCP resource to AppHost.cs**

Add before the `builder.Build().Run();` line in `src/IAW.AppHost/AppHost.cs`:

```csharp
builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WaitFor(samples);
```

**Step 4: Build to verify**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: BUILD SUCCEEDED

**Step 5: Commit**

```bash
git add src/IAW.MCP/MCP.csproj src/IAW.AppHost/Aspire.csproj src/IAW.AppHost/AppHost.cs
git commit -m "feat: convert MCP to Aspire-managed Orleans client project"
```

---

### Task 5: Wire MCP Program.cs with Orleans client

**Files:**
- Modify: `src/IAW.MCP/Program.cs`

**Step 1: Replace Program.cs with Orleans client setup**

Replace the entire `src/IAW.MCP/Program.cs` with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.UseOrleansClient(client =>
{
    client.AddMemoryStreams("agents");
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<AgentTools>();

await builder.Build().RunAsync();
```

**Step 2: Build to verify**

Run: `dotnet build src/IAW.MCP/MCP.csproj`
Expected: BUILD FAILED — `AgentTools` class doesn't exist yet. This is expected.

**Step 3: Commit**

```bash
git add src/IAW.MCP/Program.cs
git commit -m "feat: wire MCP Program.cs with Orleans client and MCP server"
```

---

### Task 6: Create AgentTools MCP tools class

**Files:**
- Create: `src/IAW.MCP/Tools/AgentTools.cs`
- Delete: `src/IAW.MCP/Tools/RandomNumberTools.cs`

**Step 1: Create AgentTools.cs**

Create `src/IAW.MCP/Tools/AgentTools.cs`:

```csharp
using Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

internal sealed class AgentTools(IClusterClient orleans)
{
    private static readonly string[] WellKnownAgentIds =
    [
        "personal-assistant",
        "roslyn",
        "dotnet",
        "nuget",
        "github",
        "reviewer",
        "self-improvement",
        "fs",
        "shell",
        "git",
        "build",
        "knowledge",
        "user",
        "planning",
        "notification"
    ];

    [McpServerTool(Name = "agent_list_all")]
    [Description("List all registered agents with their metadata and capabilities.")]
    public async Task<string> AgentListAll(CancellationToken ct)
    {
        var results = new List<AgentMetadata>();
        foreach (var id in WellKnownAgentIds)
        {
            var agent = orleans.GetGrain<IAgent>(id);
            var metadata = await agent.GetMetadataAsync(ct);
            results.Add(metadata);
        }

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "assistant_chat")]
    [Description("Send a message to the PersonalAssistant agent. Records the message in the agent's conversation history.")]
    public async Task<string> AssistantChat(
        [Description("The message to send to the assistant")] string message,
        CancellationToken ct)
    {
        var assistant = orleans.GetGrain<IAgent>("personal-assistant");
        await assistant.AddHistoryAsync("user", message, ct);
        var history = await assistant.GetHistoryAsync(ct);
        return JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

Note: `assistant_chat` uses `AddHistoryAsync` to record the message (same pattern as TelegramBot). Full LLM response flow will be added when a `ChatAsync` grain interface method is implemented.

**Step 2: Delete RandomNumberTools.cs**

Delete `src/IAW.MCP/Tools/RandomNumberTools.cs`.

**Step 3: Build to verify**

Run: `dotnet build src/IAW.MCP/MCP.csproj`
Expected: BUILD SUCCEEDED

**Step 4: Commit**

```bash
git add src/IAW.MCP/Tools/AgentTools.cs
git rm src/IAW.MCP/Tools/RandomNumberTools.cs
git commit -m "feat: add AgentTools with agent_list_all and assistant_chat MCP tools"
```

---

### Task 7: Full build + test verification

**Files:** None — verification only

**Step 1: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED for all projects

**Step 2: Run unit tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All 41 tests pass

**Step 3: Run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: All tests pass. If endpoint discovery fails (because we removed `orleans-gateway` manual endpoint), the `AspireAgentTest.OrleansSiloEndpointName` property may need updating — see Task 8.

**Step 4: Start with Aspire**

Run: `aspire run`
Expected: All resources start. Verify in Aspire dashboard:
- `samples` starts as Orleans silo
- `telegram-bot` starts and joins the cluster
- `mcp` starts as Orleans client
- `devui` starts

**Step 5: Commit if any fixes were needed**

```bash
git add -A
git commit -m "fix: resolve build/test issues from Orleans config refactoring"
```

---

### Task 8: Fix AspireAgentTest if integration tests fail (conditional)

**Files:**
- Modify: `src/IAW.Testing/AspireAgentTest.cs:29` (endpoint name)

Only needed if Task 7 Step 3 fails due to endpoint name changes.

**Step 1: Check what endpoint names Aspire auto-generates**

When manual `.WithEndpoint("orleans-gateway", ...)` is removed, Aspire generates its own endpoint names. The `AspireAgentTest` at line 29 uses:
```csharp
protected virtual string OrleansSiloEndpointName => "orleans-gateway";
```

If this fails, check the Aspire dashboard or logs for the actual endpoint name and update accordingly. Common Aspire-generated names include `http`, `https`, or the default Orleans endpoint names.

**Step 2: Update endpoint name if needed**

If the endpoint name changed, update line 29:
```csharp
protected virtual string OrleansSiloEndpointName => "<actual-aspire-endpoint-name>";
```

**Step 3: Re-run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/IAW.Testing/AspireAgentTest.cs
git commit -m "fix: update AspireAgentTest endpoint name for Aspire-managed Orleans"
```

---

### Task 9: Clean up MCP sample artifacts (optional)

**Files:**
- Delete: `src/IAW.MCP/.mcp/server.json` (standalone NuGet MCP metadata — no longer relevant)
- Delete: `src/IAW.MCP/README.md` (sample README for standalone MCP tool)

**Step 1: Remove sample artifacts**

```bash
git rm src/IAW.MCP/.mcp/server.json src/IAW.MCP/README.md 2>/dev/null || true
```

**Step 2: Commit**

```bash
git commit -m "chore: remove standalone MCP packaging artifacts"
```
