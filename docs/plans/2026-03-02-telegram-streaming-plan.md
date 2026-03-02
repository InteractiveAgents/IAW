# Telegram Bot Streaming Responses Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Stream LLM responses to Telegram users in real time using `sendMessageDraft`, replacing the current stub that never returns a response.

**Architecture:** TelegramBotGrain gets `[Llm<Claude45Haiku>]` injected, calls its own `SendAsync()` which yields `IAsyncEnumerable<string>` tokens locally. Tokens are batched on a 400ms throttle and streamed via `SendMessageDraftAsync`. A final `SendMessageAsync` commits the permanent message. The `HandleUpdate` method is `[OneWay]` (fire-and-forget), so streaming happens inside the grain without blocking the webhook.

**Tech Stack:** Orleans 10.0, Telegram.BotAPI 9.4.0 (`SendMessageDraftAsync`), Microsoft.Extensions.AI (`IChatClient`), Claude 4.5 Haiku via `[Llm<Claude45Haiku>]`

---

### Task 1: Wire LLM into TelegramBotGrain

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramBotGrain.cs:1-26`
- Modify: `src/Clients.Telegram.Bot/Program.cs:9-25`

**Step 1: Add LLM provider registration to Program.cs**

In `src/Clients.Telegram.Bot/Program.cs`, add `using Core.AI;` to the imports and call `builder.AddLlmProviders();` right after the Orleans silo configuration block (after line 25), before the Telegram services block:

```csharp
using Core;
using Core.AI;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using ServiceDefaults;
using Telegram.BotAPI;
using TelegramBot;
using BotUpdate = Telegram.BotAPI.GettingUpdates.Update;

var builder = WebApplication.CreateBuilder(args);

var siloPort = builder.Configuration.GetValue("Orleans:Endpoints:SiloPort", 11_112);
var gatewayPort = builder.Configuration.GetValue("Orleans:Endpoints:GatewayPort", 30_001);
var clusterId = builder.Configuration.GetValue("Orleans:ClusterId", "dev");
var serviceId = builder.Configuration.GetValue("Orleans:ServiceId", "dev");

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering(
        siloPort: siloPort,
        gatewayPort: gatewayPort,
        serviceId: serviceId,
        clusterId: clusterId);
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
});

builder.AddLlmProviders();

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));
// ... rest unchanged
```

**Step 2: Add LLM constructor parameter and activation to TelegramBotGrain**

In `src/Clients.Telegram.Bot/TelegramBotGrain.cs`, add the `[Llm<Claude45Haiku>]` parameter, `SystemPrompt`/`DisplayName` overrides, and `OnActivateAsync`:

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

public sealed class TelegramBotGrain(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    ITelegramBotClient bot,
    ILogger<TelegramBotGrain> logger)
    : Agent(values, history, events, subscriptions, notifications, tracking),
      Core.ITelegramBot
{
    public override string DisplayName => "Telegram Bot";
    public override string SystemPrompt => "You are a helpful AI assistant in a Telegram chat. Keep responses concise and well-formatted for mobile reading.";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        Activate(chatClient);
    }

    // ... rest of class unchanged
```

**Step 3: Add WithLLMEnvironment to AppHost**

In `src/IAW.AppHost/AppHost.cs`, add `.WithLLMEnvironment(builder)` to the telegram-bot project declaration so it receives LLM API key env vars:

```csharp
var telegramBot = builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));
```

**Step 4: Build to verify compilation**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 errors.

**Step 5: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramBotGrain.cs src/Clients.Telegram.Bot/Program.cs src/IAW.AppHost/AppHost.cs
git commit -m "feat: wire Claude 4.5 Haiku LLM into TelegramBotGrain"
```

---

### Task 2: Implement StreamResponseAsync

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramBotGrain.cs` (add new method after `SendTyping`)

**Step 1: Add the streaming method**

Add this method to `TelegramBotGrain` after the `SendTyping` method (after line 129). This method:
- Calls `SendAsync(message)` which yields `IAsyncEnumerable<string>` tokens
- Batches tokens on a 400ms throttle
- Calls `SendMessageDraftAsync` for each batch
- Re-sends typing indicator every 4 seconds
- Finalizes with `SendMessageAsync` when stream completes
- Handles errors gracefully

```csharp
    private async Task StreamResponseAsync(long chatId, int? threadId, string userMessage, CancellationToken ct)
    {
        var draftId = Random.Shared.Next(1, int.MaxValue);
        var accumulated = new StringBuilder();
        var throttle = TimeSpan.FromMilliseconds(400);
        var typingInterval = TimeSpan.FromSeconds(4);
        var lastDraftUpdate = DateTimeOffset.MinValue;
        var lastTypingUpdate = DateTimeOffset.UtcNow;

        try
        {
            await foreach (var token in SendAsync(userMessage, ct))
            {
                accumulated.Append(token);
                var now = DateTimeOffset.UtcNow;

                if (now - lastTypingUpdate >= typingInterval)
                {
                    try { await SendTyping(chatId, threadId, ct); }
                    catch (BotRequestException) { }
                    lastTypingUpdate = now;
                }

                if (now - lastDraftUpdate >= throttle)
                {
                    try
                    {
                        await bot.SendMessageDraftAsync(chatId, draftId, accumulated.ToString(),
                            messageThreadId: threadId, cancellationToken: ct);
                    }
                    catch (BotRequestException ex) when (ex.ErrorCode == 429)
                    {
                        logger.LogWarning("Rate limited during streaming, skipping draft update");
                    }
                    catch (BotRequestException ex)
                    {
                        logger.LogWarning(ex, "Draft update failed, continuing to accumulate");
                    }
                    lastDraftUpdate = now;
                }
            }

            var finalText = accumulated.Length > 0 ? accumulated.ToString() : "I couldn't generate a response.";
            await bot.SendMessageAsync(chatId, finalText,
                messageThreadId: threadId, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Streaming failed for chat {ChatId}", chatId);
            var errorText = accumulated.Length > 0
                ? accumulated + "\n\n(Response was interrupted)"
                : "Sorry, something went wrong. Please try again.";
            try { await SendText(chatId, errorText, threadId, ct); }
            catch (BotRequestException) { }
        }
    }
```

**Step 2: Build to verify**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramBotGrain.cs
git commit -m "feat: add StreamResponseAsync with sendMessageDraft throttling"
```

---

### Task 3: Rewrite HandleTextMessage to use streaming

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramBotGrain.cs:230-260`

**Step 1: Replace HandleTextMessage**

Replace the current `HandleTextMessage` method (lines 230-260) with a version that calls `StreamResponseAsync` instead of delegating to PersonalAssistant. The bot now IS the assistant — it has its own LLM and conversation history.

Old code to replace:
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

        if (update.ThreadId == registry?.AssistantThreadId)
        {
            var assistant = GrainFactory.GetGrain<IAgent>("personal-assistant");
            await assistant.AddHistoryAsync("user", update.Text!, ct);
            await SendText(update.ChatId, "Message received. Processing...", update.ThreadId, ct);
            return;
        }

        if (update.ThreadId == registry?.SettingsThreadId)
        {
            await SendText(update.ChatId, "Settings: coming soon.", update.ThreadId, ct);
            return;
        }

        var generalAssistant = GrainFactory.GetGrain<IAgent>("personal-assistant");
        await generalAssistant.AddHistoryAsync("user", update.Text!, ct);
        await SendText(update.ChatId, "Received.", update.ThreadId, ct);
    }
```

New code:
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

        await StreamResponseAsync(update.ChatId, update.ThreadId, update.Text!, ct);
    }
```

**Step 2: Build to verify**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramBotGrain.cs
git commit -m "feat: replace stub response with streaming LLM in HandleTextMessage"
```

---

### Task 4: Build, run, and verify end-to-end

**Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 errors.

**Step 2: Run unit tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All 41 tests pass (streaming changes are in TelegramBot, not Core).

**Step 3: Run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: All 18 tests pass.

**Step 4: Start Aspire and test manually**

Run: `aspire run`

Verify in Aspire dashboard:
- `telegram-bot` resource starts successfully
- `telegram-bot` receives `AI__LLM__*` env vars (check resource details)
- `telegram-bot` shows logs and traces in dashboard (ServiceDefaults now wired)
- Send a message to @jarvis_autonomus_ai_bot in the Assistant topic
- Bot should stream the response progressively via drafts, then finalize

**Step 5: Commit if any final adjustments needed**

```bash
git add -A
git commit -m "fix: adjustments from end-to-end testing"
```
