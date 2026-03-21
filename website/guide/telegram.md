# Telegram Bot

The IAW Telegram client connects your agent runtime to Telegram, letting users chat with agents, receive approval prompts with inline buttons, upload documents and photos, send voice messages, and receive documents and media back — all through a standard Telegram chat.

## Architecture

The Telegram client runs as a separate ASP.NET Core process that connects to the Orleans silo as a client:

```
Telegram API → Ngrok → /webhook endpoint → TelegramBotService
                                                ↓
                                          IThread grain (Orleans silo)
                                                ↓
                                          Agent.GetResponseStream()
                                                ↓
                                          Delegate tool → AgentSelectorAgent
                                                ↓
                                          CodeOrchestratorAgent → Specialized agents
```

Key components:

| Component | Role |
|-----------|------|
| `TelegramBotService` | Handles webhook updates, streams responses, renders UI buttons, sends outgoing media |
| `StreamSubscriber` | Listens to Orleans streams for notification/orchestration events |
| `WebhookSetupService` | Auto-discovers ngrok URL and registers the Telegram webhook |

Each Telegram user gets their own `IThread` grain (keyed by `{telegramId}/{projectName}`), which provides per-user conversation history, tasks, scheduled jobs, and context enrichment.

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
3. The bot sets a reaction on the incoming message as visual acknowledgment
4. `TelegramBotService` checks for pending UI inputs (`IUISession`), resolves the user's `IThread` grain, and calls `thread.GetResponseStream()`
5. Response chunks stream back and are rendered via `editMessageText` with 1500ms throttling
6. If the response exceeds 4000 characters, it splits into continuation messages

## Delegation Progress

When the Thread agent delegates a task to CodeOrchestrator, real-time progress events update the user:

1. CodeOrchestrator publishes `orchestration.progress` events at each phase (planning, building, executing)
2. StreamSubscriber routes these to `TelegramBotService.SendProgressAsync`
3. The first event sends a new message (triggers push notification)
4. Subsequent events edit that message in-place
5. On completion, the progress message is replaced with a structured result card with follow-up buttons

## Structured Job Results

Delegated task results are formatted as structured cards via `OrchestrationResult`:

- Success/failure icon with summary
- Artifact file paths
- Follow-up suggestion buttons (via TelegramUIAgent)

Results that cannot be parsed as `OrchestrationResult` (single-agent responses) fall back to existing TelegramUIAgent formatting.

## Per-User Threads

Each Telegram user gets an isolated `IThread` grain with:

- **Conversation history** — durable chat history with automatic summarization at 40 messages
- **Context enrichment** — user preferences, project tasks, and RAG context are injected into system instructions (not the user prompt), keeping conversation history clean
- **Tools** — the LLM can call `RequestApprovalTool`, `AddTaskTool`, `ScheduleJobTool`, `Delegate`, and others
- **Scheduled jobs** — recurring tasks that run on Orleans reminders and deliver results back to the user
- **Task delegation** — the Thread agent delegates complex tasks via the `Delegate` tool, which routes through `AgentSelectorAgent` to `CodeOrchestratorAgent` and specialized agents

## Message Reactions

When the bot receives an incoming message, it immediately sets a reaction on it as a visual acknowledgment to the user. This provides instant feedback that the message has been received and is being processed, even before the LLM begins generating a response.

## Voice Transcription

Voice messages are transcribed locally using Whisper via Foundry Local, removing the dependency on external transcription APIs. The OGG audio is converted to WAV, then passed to the local Whisper model for transcription. The transcribed text is processed as a regular message.

## Outgoing Media

The bot can send files and media back to users in response to agent actions:

- `SendDocumentAsync` — sends a document file (PDF, ZIP, etc.) to the chat
- `SendPhotoAsync` — sends a photo/image to the chat
- `SendBlobAsDocumentAsync` — retrieves a file from blob storage and sends it as a document

## PDF Document Processing

Uploaded PDF documents are processed for retrieval-augmented generation (RAG):

1. PDF text is extracted using PdfPig
2. Extracted text is chunked and ingested into Qdrant vector storage
3. When the user asks questions, relevant chunks are retrieved from Qdrant and injected into the agent's context

## Orchestration Progress

The `StreamSubscriber` listens to the `orchestration.progress` Orleans stream, forwarding real-time updates about multi-step orchestration tasks back to the Telegram chat. Progress events are edited in-place so the user sees each phase update without a flood of new messages. When the task completes, the progress message is replaced with a structured `OrchestrationResult` card delivered via the `job.completed` stream.
