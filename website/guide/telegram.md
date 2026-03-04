# Telegram Bot

The `TelegramConversation` is an IAW agent that bridges Telegram with the Orleans agent runtime. It extends the `AgentV2` base class (via the V1 `Agent` adapter) and implements `ITelegramConversation`, giving it full access to agent messages, memory, events, and notifications alongside Telegram-specific messaging capabilities.

## Overview

The bot uses Telegram's **forum topics** feature to organize conversations into channels:

- **Assistant** -- routes messages to the `personal-assistant` agent via the `AgentRouter`
- **Notifications** -- receives agent alerts
- **Team** -- engineering team collaboration
- **Settings** -- configuration (coming soon)

When a user sends `/start`, the bot creates these forum topics in the group chat and presents an inline keyboard for navigation.

## Features

- **LLM-powered conversations** via `AgentRouter` with Qdrant-based semantic routing
- **Voice message transcription** using Whisper (via `IVoiceTranscriptionService`)
- **Monitor subscriptions** with scheduled ticks and notifications
- **User preference tracking** with notification-based persistence

## Setup

### 1. Create a Bot with BotFather

1. Open [@BotFather](https://t.me/BotFather) in Telegram
2. Send `/newbot` and follow the prompts
3. Copy the bot token

### 2. Enable Topics in Your Group

1. Create a Telegram group (or supergroup)
2. Go to group settings and enable **Topics**
3. Add your bot to the group and make it an admin

### 3. Configure in Aspire

The bot token is passed as an Aspire secret parameter. In your AppHost:

```csharp
var qdrant = builder.AddQdrant("qdrant")
    .WithLifetime(ContainerLifetime.Persistent);

var botToken = builder.AddParameter("bot-token", secret: true);

builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithReference(qdrant)
    .WithLLMEnvironment(builder)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"));
```

### 4. Webhook Setup

The `WebhookSetupService` hosted service auto-discovers the ngrok tunnel URL via the `Telegram__NgrokApiUrl` environment variable and registers the webhook with Telegram automatically on startup.

## How It Works

### /start Flow

1. User sends `/start` in the group
2. Bot sets an eyes reaction on the message
3. Bot creates forum topics (Assistant, Notifications, Team, Settings) and persists the registry in agent memory
4. Bot sends a welcome message with an inline keyboard
5. Bot sends a prompt in the Assistant topic

### Message Routing

When a text message arrives, the bot checks which forum topic it belongs to:

- **Assistant topic**: routes through `AgentRouter` for semantic agent matching
- **Settings topic**: handles preference changes
- **Other/General**: routes to assistant as a general message

### Voice Messages

When a voice message arrives:
1. The bot downloads the Telegram voice file (OGG format)
2. `IAudioConverter` converts OGG to WAV
3. `IVoiceTranscriptionService` transcribes the audio using Whisper
4. The transcribed text is processed as a regular text message

### Monitor Subscriptions

Users can request tracking by using keywords like "track" or "monitor" in their messages. The bot:
1. Detects tracking intent via regex
2. Parses the desired interval (e.g. "every 30 seconds")
3. Creates a monitor grain with `StartScheduleAsync`
4. Delivers periodic updates back to the Telegram thread

## ITelegramConversation Interface

```csharp
public interface ITelegramConversation : IAgent
{
    [OneWay]
    Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct = default);

    Task<TelegramSendResult> SendText(
        long chatId, string text, int? threadId = null, CancellationToken ct = default);

    Task<TelegramSendResult> SendMarkdown(
        long chatId, string markdown, int? threadId = null, CancellationToken ct = default);

    Task<TelegramSendResult> SendKeyboard(
        long chatId, string text, TelegramInlineButton[][] buttons,
        int? threadId = null, CancellationToken ct = default);

    Task<TelegramSendResult> EditMessage(
        long chatId, int messageId, string text,
        TelegramInlineButton[][]? buttons = null, CancellationToken ct = default);

    Task SendTyping(long chatId, int? threadId = null, CancellationToken ct = default);
    Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct = default);
    Task PinMessage(long chatId, int messageId, int? threadId = null, CancellationToken ct = default);
    Task<int> CreateTopic(long chatId, string name, CancellationToken ct = default);
    Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct = default);
    Task SetWebhook(string url, string? secretToken = null, CancellationToken ct = default);
    Task AnswerCallback(string callbackQueryId, string? text = null, CancellationToken ct = default);
}
```

## Contract Types

### TelegramBotUpdate

```csharp
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
    [Id(9)] public string? VoiceFileId { get; set; }
    [Id(10)] public int VoiceDuration { get; set; }
    [Id(11)] public string? CorrelationId { get; set; }
    [Id(12)] public string? TraceId { get; set; }
    [Id(13)] public string? ParentSpanId { get; set; }
    [Id(14)] public bool TraceSampled { get; set; }
}
```

### TelegramSendResult

```csharp
[GenerateSerializer]
public sealed class TelegramSendResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public string? Error { get; set; }

    public static TelegramSendResult Ok(int messageId);
    public static TelegramSendResult Fail(string error);
}
```

### TelegramTopicRegistry

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
