# IAW Telegram Bot Design

## Overview

Personal assistant Telegram bot (`src/Clients.Telegram.Bot`) that connects to the IAW agent cluster as an Orleans grain. The bot IS an agent (`ITelegramBot : IAgent`), enabling any IAW agent to send messages back to the user through Telegram.

## Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Purpose | Personal assistant | Single-user bot for task delegation, notifications, agent interaction |
| Architecture | Thin grain + handler | Bot grain wraps Telegram API; PersonalAssistant is the brain |
| Topics | Hybrid (fixed + dynamic) | Fixed: Assistant, Notifications, Settings. Dynamic: per-task on demand |
| Connection | Bot is a grain (IAgent) | Lives inside silo, other agents call it directly |
| Updates | Webhook | Cloudflare tunnel, Aspire secrets for token/secret |
| UI | Full rich kit | Inline keyboards, Markdown, typing, reactions, editing, pinning |
| Users | Single user | No per-user grain needed, one chatId from config |
| Telegram SDK | Telegram.BotAPI 9.4.0 | Same as TripRadar reference, stable, forum topic support |

## Project Structure

```
src/Clients.Telegram.Bot/
├── TelegramBot.csproj              # Web project, refs Core + Telegram.BotAPI
├── Program.cs                       # Host, DI, webhook endpoint, config binding
├── ITelegramBot.cs                  # Grain interface + models
├── TelegramBotGrain.cs             # Grain impl: API, topics, routing, formatting
└── WebhookSetupService.cs          # IHostedService: webhook registration on startup
```

## Grain Interface

```csharp
public interface ITelegramBot : IAgent
{
    // Receiving updates
    [OneWay] Task HandleUpdate(BotUpdate update);

    // Sending messages (rich UI)
    Task SendText(long chatId, string text, int? threadId = null);
    Task SendMarkdown(long chatId, string markdown, int? threadId = null);
    Task SendKeyboard(long chatId, string text, InlineButton[][] buttons, int? threadId = null);
    Task EditMessage(long chatId, int messageId, string text, InlineButton[][]? buttons = null);
    Task SendTyping(long chatId, int? threadId = null);
    Task SetReaction(long chatId, int messageId, string emoji);
    Task PinMessage(long chatId, int messageId, int? threadId = null);

    // Forum topics
    Task<int> CreateTopic(long chatId, string name);
    Task<TopicRegistry> EnsureTopics(long chatId);

    // Webhook management
    Task SetWebhook(string url);
}
```

## Models

```csharp
// Stored in grain state
record TopicRegistry(
    int AssistantThreadId,
    int NotificationsThreadId,
    int SettingsThreadId,
    Dictionary<string, int> TaskTopics  // dynamic task threads
);

// Webhook update wrapper
record BotUpdate(
    long ChatId,
    int MessageId,
    int? ThreadId,
    string? Text,
    string? CallbackData,
    string? Username
);

// Inline keyboard button
record InlineButton(string Text, string CallbackData);
```

## Update Flow

```
Cloudflare Tunnel
  → POST /webhook
  → Validate X-Telegram-Bot-API-Secret-Token header
  → Parse Update → BotUpdate
  → Get ITelegramBot grain ("bot")
  → bot.HandleUpdate(update) [OneWay, fire-and-forget]
  → Return 200 OK

Inside TelegramBotGrain.HandleUpdate:
  /start command
    → EnsureTopics(chatId) — create fixed topics if missing
    → Send welcome message with inline keyboard menu

  Message in AssistantThreadId
    → Forward text to PersonalAssistant agent
    → Stream response back as Markdown messages
    → Show typing indicator while waiting

  Message in SettingsThreadId
    → Parse settings command, update config

  Callback query
    → Parse callback data (e.g., "task:approve:123")
    → Route to appropriate agent action
    → Answer callback query

  Unknown / General topic
    → Forward to PersonalAssistant as general message
```

## Topic Management

Fixed topics (created on /start):
- **Assistant** — main conversation thread with PersonalAssistant
- **Notifications** — read-only alerts from all agents (task completions, errors, etc.)
- **Settings** — bot configuration and preferences

Dynamic topics (created by PersonalAssistant):
- **Task: {name}** — created when assistant decides a task is complex enough
- Stored in `TopicRegistry.TaskTopics` dictionary
- Cleaned up when task completes (optional)

## AppHost Integration

```csharp
// AppHost.cs
var bot = builder.AddProject<TelegramBot>("telegram-bot")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WaitFor(samples);

// Aspire secrets
builder.AddParameter("telegram-token", secret: true);
builder.AddParameter("telegram-webhook-secret", secret: true);
```

## Configuration

```csharp
record TelegramBotOptions
{
    public required string BotToken { get; init; }
    public required string WebhookUrl { get; init; }
    public required string WebhookSecretToken { get; init; }
    public long OwnerChatId { get; init; }  // single-user: your chatId
}
```

Bound from:
1. `appsettings.json` section `"Telegram"`
2. Environment variables `TELEGRAM__*`
3. Aspire parameters (secrets)

## WebhookSetupService

IHostedService that runs on startup:
1. Resolve webhook URL from config (Cloudflare tunnel)
2. Call `bot.SetWebhook(url)` with secret token
3. Verify via `GetWebhookInfo()`
4. Retry up to 10 times with 3s delay

## Rich UI Capabilities

| Feature | Telegram API | Usage |
|---------|-------------|-------|
| Inline keyboards | `SendMessage` + `InlineKeyboardMarkup` | Action buttons, menus, navigation |
| Markdown V2 | `SendMessage` with `ParseMode.MarkdownV2` | Formatted agent responses |
| Typing indicator | `SendChatAction(Typing)` | Show while agent processes |
| Reactions | `SetMessageReaction` | Acknowledge received messages (eyes emoji) |
| Edit message | `EditMessageText` | Update in-place (streaming effect) |
| Pin message | `PinChatMessage` | Important info in topics |
| Forum topics | `CreateForumTopic` | Thread organization |

## Dependencies

- `Telegram.BotAPI` 9.4.0
- `IAW.Core` (project reference)
- `Microsoft.Orleans.Client` (or Server if embedded)
- `Microsoft.Extensions.Hosting`
- Aspire ServiceDefaults

## Error Handling

- Webhook returns 200 immediately (fire-and-forget via `[OneWay]`)
- Grain catches Telegram API exceptions, logs, does not crash
- If forum topics not enabled → send message asking user to enable in BotFather
- If agent unreachable → send error message to user in Telegram
