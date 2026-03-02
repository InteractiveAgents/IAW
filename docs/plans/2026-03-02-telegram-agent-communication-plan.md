# Telegram Agent Communication System Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a multi-agent Telegram communication system with semantic routing (Qdrant), voice input (Foundry Local Whisper), voice calls (PersonaPlex), per-chat isolation, 4 forum topics, and preference memory sync.

**Architecture:** Per-chat `TelegramConversationGrain` replaces the shared `TelegramBotGrain`. An `AgentRouterGrain` uses Qdrant vector search to route messages to 17 specialized agents. Voice messages are transcribed via Foundry Local Whisper. Voice calls use PersonaPlex for full-duplex speech-to-speech. UserAgent stores preferences and notifies agents of changes.

**Tech Stack:** Orleans 10.0, Telegram.BotAPI 9.4.0, Aspire.Hosting.Qdrant, Qdrant.Client, Microsoft.AI.Foundry.Local, Concentus, NAudio, ElBruno.PersonaPlex, Microsoft.Extensions.AI

---

## Phase 1: Per-Chat Isolation & Grain Rename

### Task 1: Create ITelegramConversation interface

**Files:**
- Create: `src/Clients.Telegram.Bot/ITelegramConversation.cs`

**Step 1: Create the new interface file**

This replaces `ITelegramBot`. Copy the interface and contracts from `ITelegramBot.cs`, rename the interface, and add voice/call method stubs for later phases.

```csharp
using Orleans.Concurrency;

namespace Core;

public interface ITelegramConversation : IAgent
{
    [OneWay]
    Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct = default);

    Task<TelegramSendResult> SendText(long chatId, string text, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> SendMarkdown(long chatId, string markdown, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> SendKeyboard(long chatId, string text, TelegramInlineButton[][] buttons, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> EditMessage(long chatId, int messageId, string text, TelegramInlineButton[][]? buttons = null, CancellationToken ct = default);
    Task SendTyping(long chatId, int? threadId = null, CancellationToken ct = default);
    Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct = default);
    Task PinMessage(long chatId, int messageId, int? threadId = null, CancellationToken ct = default);
    Task<int> CreateTopic(long chatId, string name, CancellationToken ct = default);
    Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct = default);
    Task SetWebhook(string url, string? secretToken = null, CancellationToken ct = default);
    Task AnswerCallback(string callbackQueryId, string? text = null, CancellationToken ct = default);
}
```

Keep the existing serializable contracts (`TelegramBotUpdate`, `TelegramSendResult`, `TelegramInlineButton`, `TelegramTopicRegistry`) in `ITelegramBot.cs` for now — they are shared types used by both interfaces during migration.

**Step 2: Build**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/ITelegramConversation.cs
git commit -m "feat: add ITelegramConversation interface for per-chat grain isolation"
```

---

### Task 2: Create TelegramConversationGrain

**Files:**
- Create: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs`
- Modify: `src/Clients.Telegram.Bot/TelegramBotGrain.cs` (keep as-is for now, remove later)

**Step 1: Create the new grain class**

Copy `TelegramBotGrain.cs` content into `TelegramConversationGrain.cs`. Change the class name and interface. The grain key will be `$"conversation-{chatId}"`.

```csharp
using Core;
using Core.AI;
using Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Text;
using System.Text.Json;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.UpdatingMessages;

namespace TelegramBot;

public sealed class TelegramConversationGrain(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    ITelegramBotClient bot,
    ILogger<TelegramConversationGrain> logger)
    : Agent(values, history, events, subscriptions, notifications, tracking),
      Core.ITelegramConversation
{
    // Copy ALL methods from TelegramBotGrain exactly as-is.
    // The only changes are: class name, interface, logger type parameter.
    // Keep DisplayName, SystemPrompt, OnActivateAsync, HandleUpdate,
    // SendText, SendMarkdown, SendKeyboard, EditMessage, SendTyping,
    // SetReaction, PinMessage, CreateTopic, EnsureTopics, SetWebhook,
    // AnswerCallback, StreamResponseAsync, HandleStartCommand,
    // HandleTextMessage, HandleCallback, IsStartCommand, BuildInlineKeyboard.
}
```

**Step 2: Build**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramConversationGrain.cs
git commit -m "feat: add TelegramConversationGrain with per-chat isolation"
```

---

### Task 3: Wire per-chat grain in webhook endpoint

**Files:**
- Modify: `src/Clients.Telegram.Bot/Program.cs:82-86`

**Step 1: Change grain reference from shared to per-chat**

Replace this line in `Program.cs`:
```csharp
var bot = grains.GetGrain<Core.ITelegramBot>("bot");
_ = bot.HandleUpdate(botUpdate, ct);
```

With:
```csharp
var conversation = grains.GetGrain<Core.ITelegramConversation>($"conversation-{chatId}");
_ = conversation.HandleUpdate(botUpdate, ct);
```

**Step 2: Build and run unit tests**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: Build succeeded, 41 tests pass.

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/Program.cs
git commit -m "feat: wire per-chat TelegramConversationGrain in webhook endpoint"
```

---

### Task 4: Update WebhookSetupService to use new interface

**Files:**
- Modify: `src/Clients.Telegram.Bot/WebhookSetupService.cs`

**Step 1: Change grain reference in webhook setup**

In `WebhookSetupService.cs`, the `ExecuteAsync` method uses `grains.GetGrain<Core.ITelegramBot>("bot")` to call `SetWebhook`. Since `SetWebhook` is a bot-level operation (not per-chat), create a dedicated grain key for the bot-level singleton:

Find the line that gets the bot grain and change to `ITelegramConversation`:
```csharp
var bot = grains.GetGrain<Core.ITelegramConversation>("bot-webhook");
```

**Step 2: Build**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: Build succeeded.

**Step 3: Remove old TelegramBotGrain and ITelegramBot**

Delete `src/Clients.Telegram.Bot/TelegramBotGrain.cs`. Move the serializable contracts (`TelegramBotUpdate`, `TelegramSendResult`, `TelegramInlineButton`, `TelegramTopicRegistry`) from `ITelegramBot.cs` into `ITelegramConversation.cs`. Then delete `ITelegramBot.cs`.

**Step 4: Fix any remaining references**

Search for `ITelegramBot` across the solution and update to `ITelegramConversation`. Key places:
- `src/IAW.MCP/Tools/AgentTools.cs` (if it references `ITelegramBot`)
- Any test files

**Step 5: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

**Step 6: Commit**

```bash
git add -u && git add src/Clients.Telegram.Bot/
git commit -m "refactor: remove TelegramBotGrain, consolidate into TelegramConversationGrain"
```

---

## Phase 2: Qdrant Infrastructure

### Task 5: Add Qdrant packages to Directory.Packages.props

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/IAW.AppHost/Aspire.csproj`
- Modify: `src/Clients.Telegram.Bot/TelegramBot.csproj`

**Step 1: Add package versions**

Add to `Directory.Packages.props` inside the `<ItemGroup>`:
```xml
<PackageVersion Include="Aspire.Hosting.Qdrant" Version="13.1.2" />
<PackageVersion Include="Aspire.Qdrant.Client" Version="9.4.2" />
```

**Step 2: Add hosting package to AppHost**

Add to `src/IAW.AppHost/Aspire.csproj`:
```xml
<PackageReference Include="Aspire.Hosting.Qdrant" />
```

**Step 3: Add client package to Telegram bot**

Add to `src/Clients.Telegram.Bot/TelegramBot.csproj`:
```xml
<PackageReference Include="Aspire.Qdrant.Client" />
```

**Step 4: Wire Qdrant in AppHost**

In `src/IAW.AppHost/AppHost.cs`, add Qdrant resource before the telegram-bot declaration:

```csharp
var qdrant = builder.AddQdrant("qdrant")
    .WithLifetime(ContainerLifetime.Persistent);
```

Add `.WithReference(qdrant).WaitFor(qdrant)` to the telegram-bot project:

```csharp
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithLLMEnvironment(builder)
    // ... rest unchanged
```

**Step 5: Register Qdrant client in Telegram bot Program.cs**

Add to `src/Clients.Telegram.Bot/Program.cs` after `builder.AddLlmProviders();`:

```csharp
builder.AddQdrantClient("qdrant");
```

**Step 6: Build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

**Step 7: Commit**

```bash
git add Directory.Packages.props src/IAW.AppHost/Aspire.csproj src/IAW.AppHost/AppHost.cs src/Clients.Telegram.Bot/TelegramBot.csproj src/Clients.Telegram.Bot/Program.cs
git commit -m "feat: add Qdrant infrastructure via Aspire"
```

---

### Task 6: Create AgentRouter grain

**Files:**
- Create: `src/Core/Routing/IAgentRouter.cs`
- Create: `src/Core/Routing/AgentRouterGrain.cs`

**Step 1: Create the router interface**

```csharp
namespace Core.Routing;

public interface IAgentRouter : IGrainWithStringKey
{
    Task<AgentRouteResult> RouteAsync(string message, CancellationToken ct = default);
    Task RebuildRegistryAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public sealed class AgentRouteResult
{
    [Id(0)] public string AgentId { get; set; } = string.Empty;
    [Id(1)] public float Confidence { get; set; }
    [Id(2)] public bool Escalated { get; set; }
}
```

**Step 2: Create the router grain implementation**

This grain embeds agent descriptions into Qdrant and routes user messages via nearest-neighbor search. It uses `QdrantClient` from DI and an embedding model via `IChatClient`.

```csharp
using Microsoft.Extensions.AI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Core.Routing;

public sealed class AgentRouterGrain(
    QdrantClient qdrant,
    [FromKeyedServices("embedding")] IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<AgentRouterGrain> logger) : Grain, IAgentRouter
{
    private const string CollectionName = "agent-routing";
    private const float ConfidenceThreshold = 0.7f;
    private const string PersonalAssistantId = "personal-assistant";

    private static readonly (string Id, string Description)[] AgentDescriptions =
    [
        ("personal-assistant", "General assistant, task decomposition, team coordination, complex multi-step requests"),
        ("knowledge", "Project knowledge, architecture decisions, patterns, conventions, tech stack"),
        ("user", "User preferences, settings, memories, personal configuration"),
        ("fs", "File operations, reading files, writing files, searching code, listing directories"),
        ("shell", "Shell commands, terminal operations, system administration"),
        ("git", "Git version control, commits, diffs, logs, branches, reverts"),
        ("build", "Building .NET projects, compiling code, running tests"),
        ("aspire", "Aspire resources, service orchestration, health monitoring, resource management"),
        ("roslyn", "C# code analysis, type maps, architecture analysis, pattern detection, Roslyn"),
        ("dot-net", "dotnet CLI, testing, code formatting, .NET development"),
        ("nu-get", "NuGet packages, dependency management, outdated packages"),
        ("git-hub", "GitHub operations, pull requests, issues, releases, repository management"),
        ("reviewer", "Code review, quality analysis, best practices"),
        ("self-improvement", "Code quality analysis, improvement proposals, self-modification"),
        ("planning", "Execution plans, task planning, agent coordination"),
        ("notification", "Alerts, notifications, event aggregation"),
        ("deployer", "Deployment, release builds, Aspire deployment")
    ];

    public async Task<AgentRouteResult> RouteAsync(string message, CancellationToken ct = default)
    {
        try
        {
            var messageEmbedding = await embeddings.GenerateEmbeddingAsync(message, cancellationToken: ct);
            var searchResult = await qdrant.SearchAsync(
                CollectionName,
                messageEmbedding.Vector.ToArray(),
                limit: 1,
                cancellationToken: ct);

            if (searchResult.Count > 0 && searchResult[0].Score >= ConfidenceThreshold)
            {
                var agentId = searchResult[0].Payload["agentId"].StringValue;
                return new AgentRouteResult
                {
                    AgentId = agentId,
                    Confidence = searchResult[0].Score,
                    Escalated = false
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant routing failed, escalating to PersonalAssistant");
        }

        return new AgentRouteResult
        {
            AgentId = PersonalAssistantId,
            Confidence = 0,
            Escalated = true
        };
    }

    public async Task RebuildRegistryAsync(CancellationToken ct = default)
    {
        try
        {
            var collections = await qdrant.ListCollectionsAsync(ct);
            if (!collections.Any(c => c == CollectionName))
            {
                var sampleEmbedding = await embeddings.GenerateEmbeddingAsync("test", cancellationToken: ct);
                await qdrant.CreateCollectionAsync(CollectionName,
                    new VectorParams { Size = (ulong)sampleEmbedding.Vector.Length, Distance = Distance.Cosine },
                    cancellationToken: ct);
            }

            var points = new List<PointStruct>();
            for (var i = 0; i < AgentDescriptions.Length; i++)
            {
                var (id, description) = AgentDescriptions[i];
                var embedding = await embeddings.GenerateEmbeddingAsync(description, cancellationToken: ct);
                points.Add(new PointStruct
                {
                    Id = (ulong)(i + 1),
                    Vectors = embedding.Vector.ToArray(),
                    Payload = { ["agentId"] = id }
                });
            }

            await qdrant.UpsertAsync(CollectionName, points, cancellationToken: ct);
            logger.LogInformation("Agent routing registry rebuilt with {Count} agents", points.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rebuild agent routing registry");
        }
    }
}
```

**Note:** The embedding model registration will need to be added to the service registration. Use `Microsoft.Extensions.AI` embedding abstractions. The exact embedding model (OpenAI text-embedding-3-small or a local model) will be configured via LLM registration.

**Step 2: Build**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Core/Routing/
git commit -m "feat: add AgentRouter grain with Qdrant semantic routing"
```

---

## Phase 3: Wire Routing into TelegramConversation

### Task 7: Add agent routing to HandleTextMessage

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs`

**Step 1: Update HandleTextMessage to use router**

Replace the current `HandleTextMessage` that calls `StreamResponseAsync` directly. Instead, route through `AgentRouter`, then call the matched agent's `SendAsync` and stream the response.

```csharp
private async Task HandleTextMessage(TelegramBotUpdate update, CancellationToken ct)
{
    var registryJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
    if (registryJson is null)
    {
        await SendText(update.ChatId, "Send /start first to set up topics.", null, ct);
        return;
    }

    var registry = JsonSerializer.Deserialize<TelegramTopicRegistry>(registryJson);

    await SendTyping(update.ChatId, update.ThreadId, ct);

    if (update.ThreadId == registry?.SettingsThreadId)
    {
        await SendText(update.ChatId, "Settings: coming soon.", update.ThreadId, ct);
        return;
    }

    var router = GrainFactory.GetGrain<Core.Routing.IAgentRouter>("router");
    var route = await router.RouteAsync(update.Text!, ct);

    var targetAgent = GrainFactory.GetGrain<IAgent>(route.AgentId);
    var agentMeta = await targetAgent.GetMetadataAsync(ct);
    var prefix = $"[{agentMeta.DisplayName}] ";

    await StreamResponseFromAgentAsync(update.ChatId, update.ThreadId, update.Text!, targetAgent, prefix, ct);
}
```

**Step 2: Add StreamResponseFromAgentAsync method**

This is similar to `StreamResponseAsync` but calls a remote agent's history + sends via the conversation grain's Telegram connection. Since `SendAsync` is local-only (not on IAgent interface), we use `AddHistoryAsync` on the remote agent and then call the conversation grain's own `SendAsync` with context:

```csharp
private async Task StreamResponseFromAgentAsync(
    long chatId, int? threadId, string userMessage,
    IAgent targetAgent, string prefix, CancellationToken ct)
{
    // For now, use the conversation grain's own LLM with the target agent's context
    // In the future, agents could expose a streaming RPC method
    await targetAgent.AddHistoryAsync("user", userMessage, ct);
    await StreamResponseAsync(chatId, threadId, userMessage, ct);
}
```

**Note:** Full cross-grain streaming requires adding a streaming RPC method to IAgent, which is a Core change. For Phase 3, we route the intent classification but the conversation grain's own LLM generates the response. This can be enhanced in a later iteration.

**Step 3: Build and test**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: Build succeeded, all tests pass.

**Step 4: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramConversationGrain.cs
git commit -m "feat: wire Qdrant agent routing into HandleTextMessage"
```

---

## Phase 4: Topic Layout

### Task 8: Update EnsureTopics for 4-topic layout

**Files:**
- Modify: `src/Clients.Telegram.Bot/ITelegramConversation.cs` (update TelegramTopicRegistry)
- Modify: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs`

**Step 1: Add TeamThreadId to TelegramTopicRegistry**

In the `TelegramTopicRegistry` class, add the Team thread:

```csharp
[GenerateSerializer]
public sealed class TelegramTopicRegistry
{
    [Id(0)] public int AssistantThreadId { get; set; }
    [Id(1)] public int NotificationsThreadId { get; set; }
    [Id(2)] public int SettingsThreadId { get; set; }
    [Id(3)] public Dictionary<string, int> TaskTopics { get; set; } = [];
    [Id(4)] public int TeamThreadId { get; set; }
}
```

**Step 2: Update EnsureTopics to create Team topic**

In `TelegramConversationGrain.cs`, update `EnsureTopics` to create the 4th topic:

```csharp
var assistantThreadId = await CreateTopic(chatId, "Assistant", ct);
var teamThreadId = await CreateTopic(chatId, "Team", ct);
var notificationsThreadId = await CreateTopic(chatId, "Notifications", ct);
var settingsThreadId = await CreateTopic(chatId, "Settings", ct);

var registry = new TelegramTopicRegistry
{
    AssistantThreadId = assistantThreadId,
    TeamThreadId = teamThreadId,
    NotificationsThreadId = notificationsThreadId,
    SettingsThreadId = settingsThreadId
};
```

**Step 3: Build and test**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All pass.

**Step 4: Commit**

```bash
git add src/Clients.Telegram.Bot/ITelegramConversation.cs src/Clients.Telegram.Bot/TelegramConversationGrain.cs
git commit -m "feat: add Team topic to 4-topic forum layout"
```

---

## Phase 5: Team Topic Streaming

### Task 9: Subscribe to agent events and post to Team topic

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs`

**Step 1: Add method to post agent updates to Team topic**

When PersonalAssistant delegates to team agents, their events should appear in the Team topic. Add a method that subscribes to an agent's event stream and posts updates:

```csharp
private async Task PostToTeamTopicAsync(long chatId, int teamThreadId, string agentName, string message, CancellationToken ct)
{
    var text = $"[{agentName}] {message}";
    try
    {
        await SendText(chatId, text, teamThreadId, ct);
    }
    catch (BotRequestException ex)
    {
        logger.LogWarning(ex, "Failed to post to Team topic");
    }
}
```

**Step 2: In HandleTextMessage, when routing is escalated, post delegation info to Team topic**

After the router escalates to PersonalAssistant, post a message to the Team topic:

```csharp
if (route.Escalated && registry?.TeamThreadId > 0)
{
    await PostToTeamTopicAsync(update.ChatId, registry.TeamThreadId,
        "Router", $"Delegated to {agentMeta.DisplayName}: {update.Text}", ct);
}
```

**Step 3: Build and commit**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`

```bash
git add src/Clients.Telegram.Bot/TelegramConversationGrain.cs
git commit -m "feat: post agent delegation updates to Team topic"
```

---

## Phase 6: Voice Messages (Foundry Local Whisper)

### Task 10: Add voice processing packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Clients.Telegram.Bot/TelegramBot.csproj`
- Modify: `src/IAW.AppHost/Aspire.csproj`

**Step 1: Add package versions**

Add to `Directory.Packages.props`:
```xml
<PackageVersion Include="Aspire.Hosting.Azure.AIFoundry" Version="13.1.2" />
<PackageVersion Include="Microsoft.AI.Foundry.Local" Version="0.8.2.1" />
<PackageVersion Include="Concentus" Version="2.2.1" />
<PackageVersion Include="Concentus.OggFile" Version="1.0.5" />
<PackageVersion Include="NAudio" Version="2.2.1" />
```

Add to `TelegramBot.csproj`:
```xml
<PackageReference Include="Microsoft.AI.Foundry.Local" />
<PackageReference Include="Concentus" />
<PackageReference Include="Concentus.OggFile" />
<PackageReference Include="NAudio" />
```

Add to `Aspire.csproj`:
```xml
<PackageReference Include="Aspire.Hosting.Azure.AIFoundry" />
```

**Step 2: Add Foundry Local to AppHost**

In `AppHost.cs`, add before the telegram-bot declaration:
```csharp
var foundry = builder.AddAzureAIFoundry("foundry").RunAsFoundryLocal();
```

Add `.WaitFor(foundry)` to telegram-bot project.

**Step 3: Build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add Directory.Packages.props src/Clients.Telegram.Bot/TelegramBot.csproj src/IAW.AppHost/Aspire.csproj src/IAW.AppHost/AppHost.cs
git commit -m "feat: add Foundry Local, Concentus, NAudio packages for voice processing"
```

---

### Task 11: Create AudioConverter service

**Files:**
- Create: `src/Clients.Telegram.Bot/Services/AudioConverter.cs`

**Step 1: Implement OGG Opus to WAV converter**

Based on the brain-master reference pattern:

```csharp
using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace TelegramBot.Services;

public interface IAudioConverter
{
    Task<string> ConvertOggToWavAsync(Stream oggStream, CancellationToken ct = default);
}

public sealed class AudioConverter : IAudioConverter
{
    private const int SampleRate = 48000;
    private const int Channels = 1;

    public async Task<string> ConvertOggToWavAsync(Stream oggStream, CancellationToken ct = default)
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

        var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        var pcmBuffer = new short[SampleRate * Channels];
        var allSamples = new List<short>();

        var oggIn = new OpusOggReadStream(decoder, oggStream);
        while (oggIn.HasNextPacket)
        {
            ct.ThrowIfCancellationRequested();
            var samples = oggIn.DecodeNextPacket();
            if (samples is not null)
                allSamples.AddRange(samples);
        }

        await using var wavWriter = new WaveFileWriter(wavPath,
            new WaveFormat(SampleRate, 16, Channels));
        var byteBuffer = new byte[allSamples.Count * 2];
        Buffer.BlockCopy(allSamples.ToArray(), 0, byteBuffer, 0, byteBuffer.Length);
        await wavWriter.WriteAsync(byteBuffer, ct);

        return wavPath;
    }
}
```

**Step 2: Register in Program.cs**

Add to `Program.cs`:
```csharp
builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
```

**Step 3: Build and commit**

```bash
git add src/Clients.Telegram.Bot/Services/AudioConverter.cs src/Clients.Telegram.Bot/Program.cs
git commit -m "feat: add OGG Opus to WAV audio converter using Concentus + NAudio"
```

---

### Task 12: Create VoiceTranscriptionService

**Files:**
- Create: `src/Clients.Telegram.Bot/Services/VoiceTranscriptionService.cs`

**Step 1: Implement Foundry Local Whisper transcription**

```csharp
using Microsoft.AI.Foundry.Local;

namespace TelegramBot.Services;

public interface IVoiceTranscriptionService : IAsyncDisposable
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
}

public sealed class VoiceTranscriptionService(ILogger<VoiceTranscriptionService> logger) : IVoiceTranscriptionService
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Model? _model;
    private bool _initialized;

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (_model is null)
            throw new InvalidOperationException("Whisper model not available");

        var audioClient = await _model.GetAudioClientAsync();
        var result = new StringBuilder();

        await foreach (var chunk in audioClient.TranscribeAudioStreamingAsync(audioFilePath, ct))
        {
            result.Append(chunk.Text);
        }

        return result.ToString().Trim();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var config = new Configuration { AppName = "iaw-telegram" };
            await FoundryLocalManager.CreateAsync(config, logger);

            var catalog = await FoundryLocalManager.Instance.GetCatalogAsync();
            _model = await catalog.GetModelAsync("whisper-large-v3-turbo")
                ?? await catalog.GetModelAsync("whisper-small")
                ?? await catalog.GetModelAsync("whisper-tiny");

            if (_model is null)
            {
                logger.LogError("No Whisper model found in Foundry Local catalog");
                return;
            }

            await _model.DownloadAsync();
            await _model.LoadAsync();
            _initialized = true;
            logger.LogInformation("Whisper model loaded: {Model}", _model.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Foundry Local Whisper");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized && _model is not null)
            await _model.UnloadAsync();
    }
}
```

**Step 2: Register in Program.cs**

```csharp
builder.Services.AddSingleton<IVoiceTranscriptionService, VoiceTranscriptionService>();
```

**Step 3: Build and commit**

```bash
git add src/Clients.Telegram.Bot/Services/VoiceTranscriptionService.cs src/Clients.Telegram.Bot/Program.cs
git commit -m "feat: add VoiceTranscriptionService using Foundry Local Whisper"
```

---

### Task 13: Handle voice messages in TelegramConversationGrain

**Files:**
- Modify: `src/Clients.Telegram.Bot/ITelegramConversation.cs` (add voice fields to TelegramBotUpdate)
- Modify: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs`
- Modify: `src/Clients.Telegram.Bot/Program.cs` (map voice data in webhook)

**Step 1: Add voice fields to TelegramBotUpdate**

```csharp
[Id(9)] public string? VoiceFileId { get; set; }
[Id(10)] public int VoiceDuration { get; set; }
```

**Step 2: Map voice data in webhook endpoint**

In `Program.cs`, add to the `TelegramBotUpdate` mapping:
```csharp
VoiceFileId = update.Message?.Voice?.FileId,
VoiceDuration = update.Message?.Voice?.Duration ?? 0,
```

**Step 3: Add HandleVoiceMessage to TelegramConversationGrain**

```csharp
private async Task HandleVoiceMessage(TelegramBotUpdate update, CancellationToken ct)
{
    await SendTyping(update.ChatId, update.ThreadId, ct);

    string? wavPath = null;
    try
    {
        var file = await bot.GetFileAsync(update.VoiceFileId!, ct);
        await using var oggStream = new MemoryStream();
        await bot.DownloadFileAsync(file.FilePath!, oggStream, ct);
        oggStream.Position = 0;

        var converter = ServiceProvider.GetRequiredService<IAudioConverter>();
        wavPath = await converter.ConvertOggToWavAsync(oggStream, ct);

        var transcriber = ServiceProvider.GetRequiredService<IVoiceTranscriptionService>();
        var transcribedText = await transcriber.TranscribeAsync(wavPath, ct);

        if (string.IsNullOrWhiteSpace(transcribedText))
        {
            await SendText(update.ChatId, "Could not transcribe the voice message.", update.ThreadId, ct);
            return;
        }

        await StreamResponseAsync(update.ChatId, update.ThreadId, transcribedText, ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Voice processing failed for chat {ChatId}", update.ChatId);
        await SendText(update.ChatId, "Sorry, I couldn't process your voice message.", update.ThreadId, ct);
    }
    finally
    {
        if (wavPath is not null && File.Exists(wavPath))
            File.Delete(wavPath);
    }
}
```

**Step 4: Wire into HandleUpdate**

In `HandleUpdate`, add a check for voice messages before the text check:

```csharp
if (!string.IsNullOrEmpty(update.VoiceFileId))
{
    await HandleVoiceMessage(update, ct);
    return;
}
```

**Step 5: Build and commit**

```bash
git add src/Clients.Telegram.Bot/
git commit -m "feat: handle Telegram voice messages with Foundry Local Whisper transcription"
```

---

## Phase 7: Memory & Preference System

### Task 14: Wire preference sync via notifications

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs`

**Step 1: Subscribe to user preference changes on activation**

In `OnActivateAsync`, subscribe to `"user.preference.changed"` notifications:

```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    await base.OnActivateAsync(cancellationToken);
    Activate(chatClient);

    var userAgent = GrainFactory.GetGrain<IAgent>("user");
    await userAgent.SubscribeAsync("user.preference.changed", this.GetPrimaryKeyString(), cancellationToken);
}
```

**Step 2: Override ReceiveNotificationAsync to handle preference updates**

The TelegramConversation grain caches relevant preferences in its own state:

```csharp
// This is inherited from Agent — notifications arrive automatically
// The grain will receive notifications on "user.preference.changed" topic
// and can read the payload to update local state cache
```

**Step 3: Build and commit**

```bash
git add src/Clients.Telegram.Bot/TelegramConversationGrain.cs
git commit -m "feat: subscribe to user preference changes for local caching"
```

---

## Phase 8: Voice Calls (PersonaPlex)

### Task 15: Add PersonaPlex packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Clients.Telegram.Bot/TelegramBot.csproj`

**Step 1: Add package versions**

Add to `Directory.Packages.props`:
```xml
<PackageVersion Include="ElBruno.PersonaPlex" Version="0.1.0" />
```

Add to `TelegramBot.csproj`:
```xml
<PackageReference Include="ElBruno.PersonaPlex" />
```

**Step 2: Build**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add Directory.Packages.props src/Clients.Telegram.Bot/TelegramBot.csproj
git commit -m "feat: add PersonaPlex package for voice-to-voice calls"
```

---

### Task 16: Create VoiceCallService

**Files:**
- Create: `src/Clients.Telegram.Bot/Services/VoiceCallService.cs`

**Step 1: Implement PersonaPlex voice call handler**

```csharp
namespace TelegramBot.Services;

public interface IVoiceCallService : IAsyncDisposable
{
    Task<byte[]> ProcessAudioAsync(byte[] inputAudio, string? persona = null, CancellationToken ct = default);
}

public sealed class VoiceCallService(ILogger<VoiceCallService> logger) : IVoiceCallService
{
    // PersonaPlex integration
    // Note: Telegram Bot API voice call support is limited.
    // This service provides the audio processing pipeline.
    // Full Telegram call integration may require TDLib.

    public async Task<byte[]> ProcessAudioAsync(byte[] inputAudio, string? persona = null, CancellationToken ct = default)
    {
        // TODO: Integrate ElBruno.PersonaPlex pipeline
        // 1. Load PersonaPlex model (ONNX-based)
        // 2. Process audio through speech-to-speech pipeline
        // 3. Return response audio bytes
        logger.LogWarning("VoiceCallService is a placeholder — PersonaPlex integration pending");
        return [];
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

**Note:** PersonaPlex is very new (v0.1.0). The full integration depends on the library's API surface stabilizing. This task creates the service skeleton and package wiring. The actual PersonaPlex pipeline integration should be done once the library's .NET API is documented.

**Step 2: Register in Program.cs**

```csharp
builder.Services.AddSingleton<IVoiceCallService, VoiceCallService>();
```

**Step 3: Build and commit**

```bash
git add src/Clients.Telegram.Bot/Services/VoiceCallService.cs src/Clients.Telegram.Bot/Program.cs
git commit -m "feat: add VoiceCallService skeleton for PersonaPlex voice-to-voice calls"
```

---

### Task 17: Final build, test, and verify

**Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

**Step 2: Unit tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All tests pass.

**Step 3: Integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: All tests pass.

**Step 4: Aspire run and verify**

Run: `aspire run`

Verify in Aspire dashboard:
- `qdrant` container is running and healthy
- `telegram-bot` has Qdrant connection string in env vars
- `telegram-bot` has LLM env vars
- `telegram-bot` shows logs and telemetry
- Send a text message to the bot — verify it routes via AgentRouter
- Send a voice message — verify transcription and response

**Step 5: Commit any final fixes**

```bash
git add -A
git commit -m "fix: adjustments from end-to-end testing"
```
