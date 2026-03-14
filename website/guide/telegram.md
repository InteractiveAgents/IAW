# Telegram Bot

The IAW Telegram client connects your agent runtime to Telegram, letting users chat with agents, receive approval prompts with inline buttons, upload documents and photos, and send voice messages — all through a standard Telegram chat.

## Architecture

The Telegram client runs as a separate ASP.NET Core process that connects to the Orleans silo as a client:

```
Telegram API → Ngrok → /webhook endpoint → TelegramBotService
                                                ↓
                                          IProject grain (Orleans silo)
                                                ↓
                                          Agent.GetResponseStream()
                                                ↓
                                          LLM (Sonnet 4.6)
```

Key components:

| Component | Role |
|-----------|------|
| `TelegramBotService` | Handles webhook updates, streams responses, renders UI buttons |
| `StreamSubscriber` | Listens to Orleans streams for approval/wizard/notification events |
| `WebhookSetupService` | Auto-discovers ngrok URL and registers the Telegram webhook |

Each Telegram user gets their own `IProject` grain (keyed by `{telegramId}/{projectName}`), which provides per-user conversation history, tasks, scheduled jobs, and context enrichment.

## Setup

### 1. Create a Bot

1. Open [@BotFather](https://t.me/BotFather) in Telegram
2. Send `/newbot` and follow the prompts
3. Copy the bot token

### 2. Configure Aspire Secrets

The bot token and ngrok auth token are Aspire secret parameters. Set them with:

```bash
dotnet user-secrets set "Parameters:bot-token" "YOUR_BOT_TOKEN"
dotnet user-secrets set "Parameters:ngrok-auth-token" "YOUR_NGROK_TOKEN"
```

### 3. Run with Aspire

```bash
dotnet run --project src/IAW.AppHost/Aspire.csproj
```

The AppHost configures the Telegram client automatically:

```csharp
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);
var botToken = builder.AddParameter("bot-token", secret: true);

builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithReference(blobs)
    .WithReference(qdrant)
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"))
    .WaitFor(assistant);

ngrok.WithTunnelEndpoint(telegram, "http");
```

On startup, `WebhookSetupService` queries the ngrok API for the public tunnel URL and registers it as the Telegram webhook.

## Message Flow

1. Telegram sends a POST to `/webhook` via the ngrok tunnel
2. The webhook handler returns 200 immediately and processes the update in the background
3. `TelegramBotService` checks for pending UI inputs (`IUISession`), resolves the user's `IProject` grain, and calls `project.GetResponseStream()`
4. Response chunks stream back and are rendered via `editMessageText` with 500ms throttling
5. If the response exceeds 4000 characters, it splits into continuation messages

## Per-User Projects

Each Telegram user gets an isolated `IProject` grain with:

- **Conversation history** — durable chat history with automatic summarization at 40 messages
- **Context enrichment** — user preferences, project tasks, and RAG context are injected into prompts
- **Tools** — the LLM can call `RequestApprovalTool`, `AddTaskTool`, `ScheduleJobTool`, and others
- **Scheduled jobs** — recurring tasks that run on Orleans reminders and deliver results back to the user
