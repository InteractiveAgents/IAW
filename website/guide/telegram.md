# Telegram Bot

The `TelegramBotGrain` is an IAW agent that bridges Telegram with the Orleans agent runtime. It extends the `Agent` base class and implements `ITelegramBot`, giving it full access to agent state, notifications, events, and streaming alongside Telegram-specific messaging capabilities.

## Overview

The bot uses Telegram's **forum topics** feature to organize conversations into channels:

- **Assistant** -- routes messages to the `personal-assistant` agent
- **Notifications** -- receives agent alerts
- **Settings** -- configuration (coming soon)

When a user sends `/start`, the bot creates these three forum topics in the group chat and presents an inline keyboard for navigation.

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
var botToken = builder.AddParameter("telegram-bot-token", secret: true);

builder.AddProject<Projects.TelegramBot>("telegram-bot")
    .WithReference(iaw)
    .WithEnvironment("Telegram__BotToken", botToken);
```

### 4. Set the Webhook

The bot exposes a webhook endpoint. After deployment, the `WebhookSetupService` hosted service calls `SetWebhook` to register the URL with Telegram:

```csharp
await bot.SetWebhook("https://your-domain.com/api/telegram/webhook", secretToken);
```

## How It Works

### /start Flow

1. User sends `/start` in the group
2. Bot sets an eyes reaction on the message
3. Bot calls `EnsureTopics` which creates three forum topics (Assistant, Notifications, Settings) and persists the topic registry in agent state
4. Bot sends a welcome message with an inline keyboard
5. Bot sends a prompt in the Assistant topic

### Topic Routing

When a text message arrives, the bot checks which forum topic it belongs to:

- **Assistant topic**: forwards the message to the `personal-assistant` agent via `AddHistoryAsync`
- **Settings topic**: responds with a placeholder
- **Other/General**: forwards to `personal-assistant` as a general message

### Callback Handling

Inline keyboard button presses trigger callback queries. The bot pattern-matches on `CallbackData` values like `nav:assistant`, `nav:notifications`, and `nav:settings` to guide users to the right topic.

## ITelegramBot Interface

The `ITelegramBot` interface extends `IAgent` with Telegram-specific methods:

```csharp
public interface ITelegramBot : IAgent
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

    Task SetReaction(
        long chatId, int messageId, string emoji, CancellationToken ct = default);

    Task PinMessage(
        long chatId, int messageId, int? threadId = null, CancellationToken ct = default);

    Task<int> CreateTopic(long chatId, string name, CancellationToken ct = default);

    Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct = default);

    Task SetWebhook(
        string url, string? secretToken = null, CancellationToken ct = default);

    Task AnswerCallback(
        string callbackQueryId, string? text = null, CancellationToken ct = default);
}
```

## API Methods

### HandleUpdate

```csharp
Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct = default);
```

Entry point for incoming Telegram updates. Marked `[OneWay]` so the webhook endpoint returns immediately. Dispatches to `/start` handling, callback handling, or text message routing.

### SendText

```csharp
Task<TelegramSendResult> SendText(long chatId, string text, int? threadId = null, CancellationToken ct = default);
```

Sends a plain text message. Pass `threadId` to target a specific forum topic.

### SendMarkdown

```csharp
Task<TelegramSendResult> SendMarkdown(long chatId, string markdown, int? threadId = null, CancellationToken ct = default);
```

Sends a message with MarkdownV2 formatting.

### SendKeyboard

```csharp
Task<TelegramSendResult> SendKeyboard(
    long chatId, string text, TelegramInlineButton[][] buttons,
    int? threadId = null, CancellationToken ct = default);
```

Sends a message with an inline keyboard. `buttons` is a jagged array where each inner array is a row of buttons.

### EditMessage

```csharp
Task<TelegramSendResult> EditMessage(
    long chatId, int messageId, string text,
    TelegramInlineButton[][]? buttons = null, CancellationToken ct = default);
```

Edits an existing message's text and optionally its inline keyboard.

### SendTyping

```csharp
Task SendTyping(long chatId, int? threadId = null, CancellationToken ct = default);
```

Sends the "typing..." chat action indicator.

### SetReaction

```csharp
Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct = default);
```

Sets an emoji reaction on a message. Used internally to acknowledge incoming messages with an eyes emoji.

### PinMessage

```csharp
Task PinMessage(long chatId, int messageId, int? threadId = null, CancellationToken ct = default);
```

Pins a message in the chat or a specific forum topic.

### CreateTopic

```csharp
Task<int> CreateTopic(long chatId, string name, CancellationToken ct = default);
```

Creates a new forum topic in the group and returns its `MessageThreadId`.

### EnsureTopics

```csharp
Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct = default);
```

Idempotent operation that creates the Assistant, Notifications, and Settings forum topics if they do not already exist. The topic registry is persisted in agent state under the key `telegram:topic-registry`.

### SetWebhook

```csharp
Task SetWebhook(string url, string? secretToken = null, CancellationToken ct = default);
```

Deletes any existing webhook, sets the new URL, and verifies it was applied correctly.

### AnswerCallback

```csharp
Task AnswerCallback(string callbackQueryId, string? text = null, CancellationToken ct = default);
```

Answers an inline keyboard callback query, optionally showing a toast notification to the user.

## Contract Types

### TelegramBotUpdate

```csharp
public sealed class TelegramBotUpdate
{
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public int? ThreadId { get; set; }
    public string? Text { get; set; }
    public string? CallbackQueryId { get; set; }
    public string? CallbackData { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public long? FromUserId { get; set; }
}
```

### TelegramSendResult

```csharp
public sealed class TelegramSendResult
{
    public bool Success { get; set; }
    public int MessageId { get; set; }
    public string? Error { get; set; }

    public static TelegramSendResult Ok(int messageId);
    public static TelegramSendResult Fail(string error);
}
```

### TelegramInlineButton

```csharp
public sealed class TelegramInlineButton
{
    public string Text { get; set; }
    public string CallbackData { get; set; }
}
```

### TelegramTopicRegistry

```csharp
public sealed class TelegramTopicRegistry
{
    public int AssistantThreadId { get; set; }
    public int NotificationsThreadId { get; set; }
    public int SettingsThreadId { get; set; }
    public Dictionary<string, int> TaskTopics { get; set; } = [];
}
```
