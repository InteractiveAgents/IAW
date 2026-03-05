# DevUI Orleans Bridge Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Bridge DevUI to Orleans IAgentV2 grains so all chat goes through the real agent pipeline, enabling GenAI telemetry.

**Architecture:** Create an `OrleansAgentChatClient : IChatClient` that routes Microsoft.Agents.AI DevUI chat to Orleans `IAgentV2.RespondAsync()` via `IClusterClient`. DevUI joins the Orleans cluster as a client (same pattern as MCP project), connecting to the samples silo via gateway.

**Tech Stack:** Orleans 10.0, Microsoft.Extensions.AI, Microsoft.Agents.AI.DevUI, .NET Aspire, OpenTelemetry

---

### Task 1: Update DevUI.csproj — swap Ollama for Orleans + Core

**Files:**
- Modify: `src/DevUI/DevUI.csproj`

**Step 1: Edit DevUI.csproj**

Replace the package/project references. Remove `CommunityToolkit.Aspire.OllamaSharp`, add `Microsoft.Orleans.Sdk` and Core project reference (same as MCP.csproj).

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UserSecretsId>f5a0e82c-0c66-43cc-8b0e-a36b06f4976a</UserSecretsId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" />
    <PackageReference Include="Microsoft.Agents.AI.DevUI" />
    <PackageReference Include="Microsoft.Agents.AI.Hosting" />
    <PackageReference Include="Microsoft.Agents.AI.Hosting.OpenAI" />
    <PackageReference Include="Microsoft.Agents.AI.OpenAI" />
    <PackageReference Include="Microsoft.Agents.AI.Workflows" />
    <PackageReference Include="Microsoft.Orleans.Sdk" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\IAW.ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

**Step 2: Verify it compiles**

Run: `dotnet build src/DevUI/DevUI.csproj`
Expected: Build succeeds (Program.cs will have errors — that's fine, we rewrite it in Task 3)

**Step 3: Commit**

```bash
git add src/DevUI/DevUI.csproj
git commit -m "chore(devui): swap Ollama package for Orleans SDK + Core reference"
```

---

### Task 2: Create OrleansAgentChatClient

**Files:**
- Create: `src/DevUI/OrleansAgentChatClient.cs`

**Step 1: Write OrleansAgentChatClient.cs**

This is the bridge from `IChatClient` to Orleans `IAgentV2` grains. It extracts the agent grain ID from `ChatOptions.Instructions` (set by `AddAIAgent` registration), calls `RespondAsync`, and returns the reply.

```csharp
using System.Runtime.CompilerServices;
using Core.V2;
using Microsoft.Extensions.AI;

namespace DevUI;

sealed class OrleansAgentChatClient(IClusterClient cluster, ILogger<OrleansAgentChatClient> logger) : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("OrleansAgentChatClient");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (agentId, userText) = ExtractAgentAndMessage(chatMessages, options);

        try
        {
            var agent = cluster.GetGrain<IAgentV2>(agentId);
            var reply = await agent.RespondAsync(
                new AgentRequest { Input = userText },
                cancellationToken);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply.Output));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orleans agent {AgentId} call failed", agentId);
            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                $"Agent '{agentId}' could not complete the request: {ex.Message}"));
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        var text = response.Messages.FirstOrDefault()?.Text ?? string.Empty;
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    static (string AgentId, string UserText) ExtractAgentAndMessage(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var agentId = options?.Instructions?.Trim();

        if (string.IsNullOrEmpty(agentId))
        {
            var messageList = messages.ToList();
            var systemMsg = messageList.FirstOrDefault(m => m.Role == ChatRole.System);
            agentId = systemMsg?.Text?.Trim();

            if (string.IsNullOrEmpty(agentId))
                throw new InvalidOperationException(
                    "Cannot determine agent ID — no Instructions or system message provided.");

            var userMsg = messageList.LastOrDefault(m => m.Role == ChatRole.User);
            return (agentId, userMsg?.Text ?? string.Empty);
        }

        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return (agentId, userMessage?.Text ?? string.Empty);
    }
}
```

**Step 2: Verify it compiles**

Run: `dotnet build src/DevUI/DevUI.csproj`
Expected: Build succeeds (or warnings only — Program.cs is not yet updated)

**Step 3: Commit**

```bash
git add src/DevUI/OrleansAgentChatClient.cs
git commit -m "feat(devui): add OrleansAgentChatClient bridging IChatClient to IAgentV2 grains"
```

---

### Task 3: Rewrite DevUI Program.cs

**Files:**
- Modify: `src/DevUI/Program.cs`

**Step 1: Rewrite Program.cs**

Replace the entire file. Key changes:
- Orleans client setup (same pattern as MCP)
- Register `OrleansAgentChatClient` as singleton `IChatClient`
- Register well-known agents via `AddAIAgent`
- Remove Ollama, remove `/assistant/respond` endpoint
- Keep DevUI, OpenAIResponses, OpenAIConversations

```csharp
using System.Net;
using DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Extensions.AI;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var gatewayAddress = builder.Configuration["Orleans:PrimaryGateway"];

builder.UseOrleansClient(client =>
{
    if (!string.IsNullOrEmpty(gatewayAddress))
    {
        var uri = new Uri(gatewayAddress);
        client.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, uri.Port));
    }
    else
    {
        client.UseLocalhostClustering();
    }
});

builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();

// Well-known agents — instructions field carries the grain ID for OrleansAgentChatClient routing
builder.AddAIAgent("personal-assistant", instructions: "personal-assistant");
builder.AddAIAgent("roslyn", instructions: "roslyn");
builder.AddAIAgent("dotnet", instructions: "dotnet");
builder.AddAIAgent("nuget", instructions: "nuget");
builder.AddAIAgent("github", instructions: "github");
builder.AddAIAgent("reviewer", instructions: "reviewer");
builder.AddAIAgent("self-improvement", instructions: "self-improvement");
builder.AddAIAgent("fs", instructions: "fs");
builder.AddAIAgent("shell", instructions: "shell");
builder.AddAIAgent("git", instructions: "git");
builder.AddAIAgent("build", instructions: "build");
builder.AddAIAgent("knowledge", instructions: "knowledge");
builder.AddAIAgent("user", instructions: "user");
builder.AddAIAgent("planning", instructions: "planning");
builder.AddAIAgent("notification", instructions: "notification");

builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

app.MapOpenAIResponses();
app.MapOpenAIConversations();

if (builder.Environment.IsDevelopment())
{
    app.MapDevUI();
}

app.Run();
```

**Step 2: Verify it compiles**

Run: `dotnet build src/DevUI/DevUI.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/DevUI/Program.cs
git commit -m "feat(devui): rewrite Program.cs to use Orleans agents via OrleansAgentChatClient"
```

---

### Task 4: Update AppHost to wire DevUI gateway endpoint

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`

**Step 1: Add Orleans gateway env var for DevUI**

The DevUI needs to know the samples silo's gateway address (same as MCP). Add `.WithEnvironment("Orleans__PrimaryGateway", ...)` and `.WaitFor(samples)`.

Change this block in AppHost.cs:
```csharp
builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder);
```

To:
```csharp
builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WithEnvironment("Orleans__PrimaryGateway", samples.GetEndpoint("orleans-gateway"))
    .WaitFor(samples);
```

**Step 2: Verify it compiles**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat(apphost): wire Orleans gateway endpoint to DevUI"
```

---

### Task 5: Update DevUI appsettings — bump trace sample ratio

**Files:**
- Modify: `src/DevUI/appsettings.json`

**Step 1: Set sample ratio to 1.0**

Change `"SampleRatio": 0.2` to `"SampleRatio": 1.0` so all traces are captured during development.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "DevUI": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "Microsoft.Agents": "Warning",
      "Polly": "Error"
    }
  },
  "Telemetry": {
    "Tracing": {
      "SampleRatio": 1.0
    }
  },
  "AllowedHosts": "*"
}
```

**Step 2: Commit**

```bash
git add src/DevUI/appsettings.json
git commit -m "chore(devui): set trace sample ratio to 1.0 for full telemetry capture"
```

---

### Task 6: Build and run end-to-end test

**Step 1: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds with no errors

**Step 2: Run aspire**

Run: `aspire run`
Expected: All resources start — samples silo, devui, telegram-bot, etc.

**Step 3: Verify DevUI connects to Orleans**

Open DevUI in browser (URL from Aspire dashboard). Select an agent (e.g. `personal-assistant`). Send a message. Verify you get a response from the Orleans agent (not an Ollama fallback).

**Step 4: Verify GenAI telemetry**

Open Aspire dashboard → Traces. Filter by `devui` or `samples` resource. You should see:
- `agent.respond` spans from `AgentV2`
- `agent.llm` spans from `AgentV2`
- `chat` / `gen_ai` spans from `Microsoft.Extensions.AI` `UseOpenTelemetry()` middleware
- Token counts in span attributes

**Step 5: Run tests**

Run: `dotnet test IAW.slnx`
Expected: All tests pass

**Step 6: Commit**

```bash
git add -A
git commit -m "feat(devui): bridge DevUI to Orleans agents with full GenAI telemetry"
```
