# Telegram Bot Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a personal assistant Telegram bot at `src/Clients.Telegram.Bot` that is an IAW Orleans grain, receives updates via webhook (Cloudflare tunnel), organizes conversations with hybrid forum topics, and provides a rich Telegram UI.

**Architecture:** The bot is a thin Orleans grain (`ITelegramBot : IAgent`) wrapping `Telegram.BotAPI 9.4.0`. A webhook endpoint receives updates, validates the secret, and fires `HandleUpdate` as `[OneWay]`. The grain manages forum topics via durable state and delegates chat messages to the PersonalAssistant agent. The project runs as an Aspire-orchestrated Orleans client connected to the main silo.

**Tech Stack:** .NET 11.0, Orleans 10.0, Telegram.BotAPI 9.4.0, Aspire 13.1.2, Cloudflare Tunnel

---

## Task 1: Create Project Skeleton

**Files:**
- Create: `src/Clients.Telegram.Bot/TelegramBot.csproj`
- Modify: `IAW.slnx` (add project reference)
- Modify: `Directory.Packages.props` (add Telegram.BotAPI package version)

**Step 1: Add Telegram.BotAPI to Directory.Packages.props**

In `Directory.Packages.props`, add inside the `<ItemGroup>`:

```xml
<PackageVersion Include="Telegram.BotAPI" Version="9.4.0" />
```

**Step 2: Create TelegramBot.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Telegram.BotAPI" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\IAW.ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>

</Project>
```

**Step 3: Add project to IAW.slnx**

Add this line to `IAW.slnx`:

```xml
<Project Path="src/Clients.Telegram.Bot/TelegramBot.csproj" />
```

**Step 4: Verify it builds**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED

**Step 5: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramBot.csproj IAW.slnx Directory.Packages.props
git commit -m "feat: add Telegram bot project skeleton with Telegram.BotAPI 9.4.0"
```

---

## Task 2: Create Grain Interface and Models

**Files:**
- Create: `src/Clients.Telegram.Bot/ITelegramBot.cs`

This file contains the grain interface and all serializable models.

**Step 1: Write ITelegramBot.cs**

```csharp
using Orleans;

namespace Core;

public interface ITelegramBot : IAgent
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

[GenerateSerializer]
public sealed class TelegramBotUpdate
{
    [Id(0)] public long ChatId { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public int? ThreadId { get; set; }
    [Id(3)] public string? Text { get; set; }
    [Id(4)] public string? CallbackQueryId { get; set; }
    [Id(5)] public string? CallbackData { get; set; }
    [Id(6)] public string? Username { get; set; }
    [Id(7)] public string? FirstName { get; set; }
    [Id(8)] public long? FromUserId { get; set; }
}

[GenerateSerializer]
public sealed class TelegramSendResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public string? Error { get; set; }

    public static TelegramSendResult Ok(int messageId) => new() { Success = true, MessageId = messageId };
    public static TelegramSendResult Fail(string error) => new() { Success = false, Error = error };
}

[GenerateSerializer]
public sealed class TelegramInlineButton
{
    [Id(0)] public string Text { get; set; } = string.Empty;
    [Id(1)] public string CallbackData { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class TelegramTopicRegistry
{
    [Id(0)] public int AssistantThreadId { get; set; }
    [Id(1)] public int NotificationsThreadId { get; set; }
    [Id(2)] public int SettingsThreadId { get; set; }
    [Id(3)] public Dictionary<string, int> TaskTopics { get; set; } = [];
}
```

**Step 2: Verify it builds**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/ITelegramBot.cs
git commit -m "feat: add ITelegramBot grain interface and serializable models"
```

---

## Task 3: Implement TelegramBotGrain

**Files:**
- Create: `src/Clients.Telegram.Bot/TelegramBotGrain.cs`

The grain wraps `TelegramBotClient`, handles updates, manages topics, and routes messages to agents.

**Step 1: Write TelegramBotGrain.cs**

```csharp
using System.Text.Json;
using Core;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Journaling;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.UpdatingMessages;

namespace TelegramBot;

public sealed class TelegramBotGrain(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<OrleansAgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<OrleansAgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<OrleansAgentNotificationRecord> notifications,
    [Memory("agent-config")] IDurableDictionary<string, OrleansAgentConfig> configurations,
    [Memory("agent-tracking")] IDurableDictionary<string, OrleansAgentTrackingStatus> tracking,
    ITelegramBotClient bot,
    ILogger<TelegramBotGrain> logger)
    : OrleansAgentGrain(values, history, events, subscriptions, notifications, configurations, tracking),
      ITelegramBot
{
    private const string TopicRegistryStateKey = "telegram:topic-registry";
    private const string StartCommand = "/start";

    public async Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct)
    {
        try
        {
            if (update.MessageId > 0)
                await SetReaction(update.ChatId, update.MessageId, "\U0001F440", ct);

            if (IsStartCommand(update.Text))
            {
                await HandleStartCommand(update.ChatId, ct);
                return;
            }

            if (!string.IsNullOrEmpty(update.CallbackData))
            {
                await HandleCallback(update, ct);
                return;
            }

            if (!string.IsNullOrWhiteSpace(update.Text))
                await HandleTextMessage(update, ct);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Telegram API error handling update from chat {ChatId}", update.ChatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error handling update from chat {ChatId}", update.ChatId);
        }
    }

    public async Task<TelegramSendResult> SendText(long chatId, string text, int? threadId, CancellationToken ct)
    {
        try
        {
            var message = await bot.SendMessageAsync(chatId, text,
                messageThreadId: threadId, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to send text to {ChatId}", chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task<TelegramSendResult> SendMarkdown(long chatId, string markdown, int? threadId, CancellationToken ct)
    {
        try
        {
            var message = await bot.SendMessageAsync(chatId, markdown,
                parseMode: FormatStyles.MarkdownV2,
                messageThreadId: threadId, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to send markdown to {ChatId}", chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task<TelegramSendResult> SendKeyboard(
        long chatId, string text, TelegramInlineButton[][] buttons, int? threadId, CancellationToken ct)
    {
        try
        {
            var keyboard = BuildInlineKeyboard(buttons);
            var message = await bot.SendMessageAsync(chatId, text,
                replyMarkup: keyboard,
                messageThreadId: threadId, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to send keyboard to {ChatId}", chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task<TelegramSendResult> EditMessage(
        long chatId, int messageId, string text, TelegramInlineButton[][]? buttons, CancellationToken ct)
    {
        try
        {
            InlineKeyboardMarkup? replyMarkup = buttons is not null ? BuildInlineKeyboard(buttons) : null;
            var message = await bot.EditMessageTextAsync(chatId, messageId, text,
                replyMarkup: replyMarkup, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to edit message {MessageId} in {ChatId}", messageId, chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task SendTyping(long chatId, int? threadId, CancellationToken ct)
    {
        await bot.SendChatActionAsync(chatId, ChatActions.Typing,
            messageThreadId: threadId, cancellationToken: ct);
    }

    public async Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct)
    {
        try
        {
            await bot.SetMessageReactionAsync(chatId, messageId,
                [new ReactionTypeEmoji(emoji)], cancellationToken: ct);
        }
        catch (BotRequestException ex)
        {
            logger.LogWarning(ex, "Failed to set reaction on message {MessageId}", messageId);
        }
    }

    public async Task PinMessage(long chatId, int messageId, int? threadId, CancellationToken ct)
    {
        await bot.PinChatMessageAsync(chatId, messageId, cancellationToken: ct);
    }

    public async Task<int> CreateTopic(long chatId, string name, CancellationToken ct)
    {
        var topic = await bot.CreateForumTopicAsync(chatId, name, cancellationToken: ct);
        return topic.MessageThreadId;
    }

    public async Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct)
    {
        var existingJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
        if (existingJson is not null)
        {
            var existing = JsonSerializer.Deserialize<TelegramTopicRegistry>(existingJson);
            if (existing is not null && existing.AssistantThreadId > 0)
                return existing;
        }

        try
        {
            var assistantThreadId = await CreateTopic(chatId, "Assistant", ct);
            var notificationsThreadId = await CreateTopic(chatId, "Notifications", ct);
            var settingsThreadId = await CreateTopic(chatId, "Settings", ct);

            var registry = new TelegramTopicRegistry
            {
                AssistantThreadId = assistantThreadId,
                NotificationsThreadId = notificationsThreadId,
                SettingsThreadId = settingsThreadId
            };

            await SetStateAsync(TopicRegistryStateKey, JsonSerializer.Serialize(registry), ct);
            return registry;
        }
        catch (BotRequestException ex) when (ex.Message.Contains("FORUM", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Chat {ChatId} does not have forum topics enabled", chatId);
            await SendText(chatId,
                "Please enable Topics in your group settings (via BotFather) and send /start again.", ct: ct);
            return new TelegramTopicRegistry();
        }
    }

    public async Task SetWebhook(string url, string? secretToken, CancellationToken ct)
    {
        await bot.DeleteWebhookAsync(cancellationToken: ct);
        var args = new SetWebhookArgs(url);
        if (!string.IsNullOrWhiteSpace(secretToken))
            args.SecretToken = secretToken;
        await bot.SetWebhookAsync(args, ct);

        var info = await bot.GetWebhookInfoAsync(ct);
        logger.LogInformation("Webhook set to {Url}, verified: {Verified}", url, info.Url == url);
    }

    public async Task AnswerCallback(string callbackQueryId, string? text, CancellationToken ct)
    {
        await bot.AnswerCallbackQueryAsync(callbackQueryId, text, cancellationToken: ct);
    }

    private async Task HandleStartCommand(long chatId, CancellationToken ct)
    {
        await SendTyping(chatId, ct: ct);
        var registry = await EnsureTopics(chatId, ct);

        if (registry.AssistantThreadId <= 0)
            return;

        var welcomeButtons = new TelegramInlineButton[][]
        {
            [new() { Text = "Chat with Assistant", CallbackData = "nav:assistant" }],
            [new() { Text = "View Notifications", CallbackData = "nav:notifications" }],
            [new() { Text = "Settings", CallbackData = "nav:settings" }]
        };

        await SendKeyboard(chatId,
            "Welcome to IAW Bot! Your topics are ready. Choose where to go:",
            welcomeButtons, ct: ct);

        await SendText(chatId, "Send me a message in the Assistant topic to start chatting.",
            registry.AssistantThreadId, ct);
    }

    private async Task HandleTextMessage(TelegramBotUpdate update, CancellationToken ct)
    {
        var registryJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
        if (registryJson is null)
        {
            await SendText(update.ChatId, "Send /start first to set up topics.", ct: ct);
            return;
        }

        var registry = JsonSerializer.Deserialize<TelegramTopicRegistry>(registryJson);

        await SendTyping(update.ChatId, update.ThreadId, ct);

        if (update.ThreadId == registry?.AssistantThreadId)
        {
            // Forward to PersonalAssistant agent
            var assistant = GrainFactory.GetGrain<IAgent>("personal-assistant");
            var chunks = await assistant.SendDeterministicAsync(update.Text!, ct);
            var response = chunks.Count > 0 ? string.Join(" ", chunks) : "I'm thinking...";
            await SendText(update.ChatId, response, update.ThreadId, ct);
            return;
        }

        if (update.ThreadId == registry?.SettingsThreadId)
        {
            await SendText(update.ChatId, "Settings: coming soon.", update.ThreadId, ct);
            return;
        }

        // General or unknown topic — forward to assistant
        var generalAssistant = GrainFactory.GetGrain<IAgent>("personal-assistant");
        var generalChunks = await generalAssistant.SendDeterministicAsync(update.Text!, ct);
        var generalResponse = generalChunks.Count > 0 ? string.Join(" ", generalChunks) : "Received.";
        await SendText(update.ChatId, generalResponse, update.ThreadId, ct);
    }

    private async Task HandleCallback(TelegramBotUpdate update, CancellationToken ct)
    {
        var registryJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
        var registry = registryJson is not null
            ? JsonSerializer.Deserialize<TelegramTopicRegistry>(registryJson)
            : null;

        var message = update.CallbackData switch
        {
            "nav:assistant" when registry?.AssistantThreadId > 0
                => $"Head to the Assistant topic (thread #{registry.AssistantThreadId}) to chat.",
            "nav:notifications" when registry?.NotificationsThreadId > 0
                => "Check the Notifications topic for agent alerts.",
            "nav:settings" when registry?.SettingsThreadId > 0
                => "Visit the Settings topic to configure your bot.",
            _ => "Unknown action."
        };

        if (!string.IsNullOrEmpty(update.CallbackQueryId))
            await AnswerCallback(update.CallbackQueryId, message, ct);

        await SendText(update.ChatId, message, ct: ct);
    }

    private static bool IsStartCommand(string? text)
        => string.Equals(text?.Trim(), StartCommand, StringComparison.OrdinalIgnoreCase);

    private static InlineKeyboardMarkup BuildInlineKeyboard(TelegramInlineButton[][] buttons)
    {
        var rows = buttons.Select(row =>
            row.Select(btn => new InlineKeyboardButton(btn.Text) { CallbackData = btn.CallbackData }).ToArray()
        ).ToArray();
        return new InlineKeyboardMarkup(rows);
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/TelegramBotGrain.cs
git commit -m "feat: implement TelegramBotGrain with update handling, topics, and rich UI"
```

---

## Task 4: Create WebhookSetupService

**Files:**
- Create: `src/Clients.Telegram.Bot/WebhookSetupService.cs`

Hosted service that registers the webhook on startup.

**Step 1: Write WebhookSetupService.cs**

```csharp
using Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace TelegramBot;

public sealed class TelegramBotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public long OwnerChatId { get; set; }
}

public sealed class WebhookSetupService(
    IGrainFactory grains,
    IOptions<TelegramBotOptions> options,
    ILogger<WebhookSetupService> logger) : BackgroundService
{
    private const int MaxRetries = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            logger.LogWarning("No webhook URL configured, skipping webhook setup");
            return;
        }

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var bot = grains.GetGrain<ITelegramBot>("bot");
                await bot.SetWebhook(config.WebhookUrl, config.WebhookSecretToken, stoppingToken);
                logger.LogInformation("Webhook registered on attempt {Attempt}: {Url}", attempt, config.WebhookUrl);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Webhook setup attempt {Attempt}/{Max} failed", attempt, MaxRetries);
                if (attempt < MaxRetries)
                    await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogError("Failed to register webhook after {Max} attempts", MaxRetries);
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/Clients.Telegram.Bot/WebhookSetupService.cs
git commit -m "feat: add WebhookSetupService and TelegramBotOptions config"
```

---

## Task 5: Create Program.cs with Webhook Endpoint

**Files:**
- Create: `src/Clients.Telegram.Bot/Program.cs`
- Create: `src/Clients.Telegram.Bot/appsettings.json`
- Create: `src/Clients.Telegram.Bot/Properties/launchSettings.json`

The host sets up Orleans client, DI, and maps the webhook endpoint.

**Step 1: Write Program.cs**

```csharp
using System.Net;
using System.Net.Sockets;
using Core;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Hosting;
using Orleans.Journaling;
using ServiceDefaults;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;
using TelegramBot;
using BotUpdate = Telegram.BotAPI.GettingUpdates.Update;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(silo =>
{
    var clusterId = builder.Configuration["IAW:Orleans:ClusterId"] ?? "default";
    var serviceId = builder.Configuration["IAW:Orleans:ServiceId"] ?? "default";
    var primarySiloEndpoint = ParseEndpoint(builder.Configuration["IAW:Orleans:PrimarySiloEndpoint"]);

    silo.UseLocalhostClustering(
        primarySiloEndpoint: primarySiloEndpoint,
        serviceId: serviceId,
        clusterId: clusterId);

    silo.AddMemoryGrainStorage("Default");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams("agents");
    silo.UseInMemoryReminderService();
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
});

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    return new TelegramBotClient(config.BotToken);
});

builder.Services.AddHostedService<WebhookSetupService>();
builder.AddServiceDefaults();

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapPost("/webhook", async (
    HttpContext context,
    IGrainFactory grains,
    IOptions<TelegramBotOptions> options,
    CancellationToken ct) =>
{
    var secret = options.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var headerValue = context.Request.Headers[TelegramConstants.XTelegramBotApiSecretToken].FirstOrDefault();
        if (!string.Equals(headerValue, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    var update = await context.Request.ReadFromJsonAsync<BotUpdate>(ct);
    if (update is null)
        return Results.BadRequest();

    var chatId = update.Message?.Chat.Id
        ?? update.CallbackQuery?.Message?.Chat.Id
        ?? 0L;

    if (chatId == 0)
        return Results.Ok();

    var botUpdate = new TelegramBotUpdate
    {
        ChatId = chatId,
        MessageId = update.Message?.MessageId ?? 0,
        ThreadId = update.Message?.MessageThreadId,
        Text = update.Message?.Text,
        CallbackQueryId = update.CallbackQuery?.Id,
        CallbackData = update.CallbackQuery?.Data,
        Username = update.Message?.From?.Username ?? update.CallbackQuery?.From?.Username,
        FirstName = update.Message?.From?.FirstName ?? update.CallbackQuery?.From?.FirstName,
        FromUserId = update.Message?.From?.Id ?? update.CallbackQuery?.From?.Id
    };

    var bot = grains.GetGrain<ITelegramBot>("bot");
    bot.HandleUpdate(botUpdate, ct);

    return Results.Ok();
});

app.MapGet("/", () => "IAW Telegram Bot");
app.Run();

static IPEndPoint? ParseEndpoint(string? endpointValue)
{
    if (string.IsNullOrWhiteSpace(endpointValue))
        return null;

    if (Uri.TryCreate(endpointValue, UriKind.Absolute, out var uri))
        return ResolveEndpoint(uri.Host, uri.Port);

    var parts = endpointValue.Split(':', 2, StringSplitOptions.TrimEntries);
    if (parts.Length == 2 && int.TryParse(parts[1], out var port))
        return ResolveEndpoint(parts[0], port);

    return null;
}

static IPEndPoint? ResolveEndpoint(string host, int port)
{
    try
    {
        var addresses = Dns.GetHostAddresses(host);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
        return address is null ? null : new IPEndPoint(address, port);
    }
    catch
    {
        return null;
    }
}
```

**Step 2: Write appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Telegram": {
    "BotToken": "",
    "WebhookUrl": "",
    "WebhookSecretToken": "",
    "OwnerChatId": 0
  }
}
```

**Step 3: Write Properties/launchSettings.json**

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**Step 4: Verify it builds**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED

**Step 5: Commit**

```bash
git add src/Clients.Telegram.Bot/Program.cs src/Clients.Telegram.Bot/appsettings.json src/Clients.Telegram.Bot/Properties/launchSettings.json
git commit -m "feat: add Program.cs with webhook endpoint and Orleans host config"
```

---

## Task 6: Wire AppHost Integration

**Files:**
- Modify: `src/IAW.AppHost/Aspire.csproj` (add project reference)
- Modify: `src/IAW.AppHost/AppHost.cs` (register telegram-bot resource)

**Step 1: Add project reference to AppHost.csproj**

Add to the `<ItemGroup>` with ProjectReferences in `src/IAW.AppHost/Aspire.csproj`:

```xml
<ProjectReference Include="..\Clients.Telegram.Bot\TelegramBot.csproj" />
```

**Step 2: Add Aspire secrets and telegram-bot resource to AppHost.cs**

Add after the `devui` block:

```csharp
var telegramToken = builder.AddParameter("telegram-bot-token", secret: true);
var telegramWebhookSecret = builder.AddParameter("telegram-webhook-secret", secret: true);
var telegramWebhookUrl = builder.AddParameter("telegram-webhook-url");
var telegramOwnerChatId = builder.AddParameter("telegram-owner-chat-id");

builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEnvironment("Telegram__BotToken", telegramToken)
    .WithEnvironment("Telegram__WebhookSecretToken", telegramWebhookSecret)
    .WithEnvironment("Telegram__WebhookUrl", telegramWebhookUrl)
    .WithEnvironment("Telegram__OwnerChatId", telegramOwnerChatId)
    .WithEndpoint("orleans-silo", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 11_112;
    })
    .WithEndpoint("orleans-gateway", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 30_001;
    })
    .WaitFor(samples);
```

**Step 3: Verify it builds**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED

**Step 4: Commit**

```bash
git add src/IAW.AppHost/Aspire.csproj src/IAW.AppHost/AppHost.cs
git commit -m "feat: wire TelegramBot into AppHost with Aspire secrets"
```

---

## Task 7: Write Tests

**Files:**
- Create: `test/TelegramBot.Tests/TelegramBot.Tests.csproj`
- Create: `test/TelegramBot.Tests/TelegramBotGrainTests.cs`
- Modify: `IAW.slnx` (add test project)

**Step 1: Create test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Clients.Telegram.Bot\TelegramBot.csproj" />
    <ProjectReference Include="..\..\src\Core\Core.csproj" />
  </ItemGroup>

</Project>
```

**Step 2: Add to IAW.slnx**

```xml
<Project Path="test/TelegramBot.Tests/TelegramBot.Tests.csproj" />
```

**Step 3: Write TelegramBotGrainTests.cs**

```csharp
using Core;
using Xunit;

namespace TelegramBot.Tests;

public class TelegramBotModelsTests
{
    [Fact]
    public void TelegramSendResult_Ok_SetsSuccessAndMessageId()
    {
        var result = TelegramSendResult.Ok(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.MessageId);
        Assert.Null(result.Error);
    }

    [Fact]
    public void TelegramSendResult_Fail_SetsErrorAndNotSuccess()
    {
        var result = TelegramSendResult.Fail("network error");

        Assert.False(result.Success);
        Assert.Equal("network error", result.Error);
    }

    [Fact]
    public void TelegramTopicRegistry_DefaultsToEmptyTaskTopics()
    {
        var registry = new TelegramTopicRegistry();

        Assert.Equal(0, registry.AssistantThreadId);
        Assert.Equal(0, registry.NotificationsThreadId);
        Assert.Equal(0, registry.SettingsThreadId);
        Assert.NotNull(registry.TaskTopics);
        Assert.Empty(registry.TaskTopics);
    }

    [Fact]
    public void TelegramBotUpdate_DefaultValues()
    {
        var update = new TelegramBotUpdate { ChatId = 123, Text = "/start" };

        Assert.Equal(123, update.ChatId);
        Assert.Equal("/start", update.Text);
        Assert.Null(update.CallbackData);
        Assert.Null(update.ThreadId);
    }

    [Fact]
    public void TelegramInlineButton_CanSetProperties()
    {
        var button = new TelegramInlineButton { Text = "Click", CallbackData = "action:click" };

        Assert.Equal("Click", button.Text);
        Assert.Equal("action:click", button.CallbackData);
    }

    [Fact]
    public void TelegramTopicRegistry_TaskTopicsCanBePopulated()
    {
        var registry = new TelegramTopicRegistry
        {
            AssistantThreadId = 1,
            NotificationsThreadId = 2,
            SettingsThreadId = 3,
            TaskTopics = new Dictionary<string, int> { ["Fix bug"] = 10, ["Deploy"] = 11 }
        };

        Assert.Equal(2, registry.TaskTopics.Count);
        Assert.Equal(10, registry.TaskTopics["Fix bug"]);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test test/TelegramBot.Tests/TelegramBot.Tests.csproj --verbosity normal`
Expected: All 6 tests pass

**Step 5: Commit**

```bash
git add test/TelegramBot.Tests/ IAW.slnx
git commit -m "test: add TelegramBot model unit tests"
```

---

## Task 8: Full Build Verification and Aspire Run

**Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: BUILD SUCCEEDED with 0 errors

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx --verbosity normal`
Expected: All tests pass

**Step 3: Aspire dev run (smoke test)**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Expected: Dashboard loads, telegram-bot resource appears. It will fail webhook setup if no token is configured — that's expected.

**Step 4: Final commit**

```bash
git add -A
git commit -m "feat: complete IAW Telegram Bot v1 with webhook, topics, and rich UI"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | Project skeleton | TelegramBot.csproj, IAW.slnx, Directory.Packages.props |
| 2 | Grain interface + models | ITelegramBot.cs |
| 3 | Grain implementation | TelegramBotGrain.cs |
| 4 | Webhook setup service | WebhookSetupService.cs |
| 5 | Program.cs + webhook endpoint | Program.cs, appsettings.json, launchSettings.json |
| 6 | AppHost wiring | Aspire.csproj, AppHost.cs |
| 7 | Tests | TelegramBot.Tests.csproj, TelegramBotGrainTests.cs |
| 8 | Full verification | Build + test + Aspire run |
