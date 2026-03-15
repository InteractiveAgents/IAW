# Telegram Bot Features

## Interactive UI Buttons

When agents need user input, they publish events to Orleans streams. The `StreamSubscriber` receives these events and renders them as Telegram inline keyboard buttons.

### Approval Prompts

Agents can request user approval via the `RequestApprovalTool`. The LLM calls the tool with a question and options, the agent publishes an `approval.requested` event, and the bot renders inline buttons:

```
User: "Deploy the new version"
Agent: [calls RequestApprovalTool]
Bot sends: 🔔 Deploy v2.1 to production?
           [Approve] [Decline] [Rollback]
```

The approval flow:

1. Agent calls `RequestApprovalTool(question, options)` during an LLM conversation
2. `Project.PublishAsync("approval.requested", ...)` sends the event to the Orleans `"agents"` stream
3. `StreamSubscriber` receives the event and calls `TelegramBotService.SendApprovalAsync()`
4. The bot registers the approval with `IUISession` and sends an `InlineKeyboardMarkup` message
5. When the user clicks a button, `HandleCallbackQueryAsync` routes it to `IUISession.HandleCallback()`
6. The `UISession` updates widget state and returns a `CallbackResult` with updated text/buttons

Callback data format: `ap:{approvalId}:{selectedOption}`

### Wizard Steps

Multi-step selection wizards work the same way via the `wizard.started` stream:

```
Bot sends: Select your deployment target:
           [Staging] [Production] [Canary]
```

Callback data format: `wz:{wizardId}:{selectedOption}`

### Other Widgets

The `IUISession` grain supports additional widget types that follow the same callback pattern:

| Widget | Callback Prefix | Purpose |
|--------|----------------|---------|
| Approval | `ap:` | Yes/no/maybe confirmation |
| Wizard | `wz:` | Multi-step option selection |
| Paginator | `pg:` | List navigation with prev/next |
| Menu | `mn:` | Hierarchical tree navigation |
| Form | `fm:` | Multi-field data collection |
| Button Grid | `bg:` | Custom button layouts |

## Voice Messages

The bot transcribes voice messages locally using Whisper via Foundry Local:

1. User sends a voice message in Telegram
2. Bot downloads the OGG file via the Telegram Bot API
3. `IAudioConverter` converts OGG to WAV
4. `IAudioTranscriptionService` transcribes the audio using a local Whisper model (no external API calls)
5. The transcribed text is processed as a regular message

## Photo Messages

Photos are uploaded to Azure Blob Storage and sent to the agent as `ImageContent`:

1. Bot downloads the highest-resolution photo variant
2. Uploads to blob storage at `{telegramId}/{projectSlug}/{guid}-photo.jpg`
3. Sends to the agent as an `ImageContent` part with the blob URI and MIME type
4. The agent's LLM processes the image alongside any caption text

## Document Messages

Documents (PDF, code files, etc.) follow a similar flow:

1. Bot downloads the document file
2. Uploads to blob storage preserving the original filename
3. Sends to the agent as a `FileContent` part with blob URI, filename, MIME type, and file size
4. Any caption text is included as an additional `TextContent` part

## Message Reactions

When the bot receives an incoming message, it immediately sets a reaction on the message as visual acknowledgment. This gives the user instant feedback that their message was received before the LLM starts generating a response.

## Outgoing Media

The bot can send files and media back to users:

| Method | Purpose |
|--------|---------|
| `SendDocumentAsync` | Sends a document file (PDF, ZIP, etc.) to the chat |
| `SendPhotoAsync` | Sends a photo or image to the chat |
| `SendBlobAsDocumentAsync` | Retrieves a file from blob storage and sends it as a document |

These methods are invoked by agents when they need to deliver generated files, reports, or media back to the user.

## Task Delegation

The Project agent's `DelegateToAssistant` tool forwards complex tasks to the `PersonalAssistantAgent`, who decomposes them and assigns subtasks to specialized agents:

```
User message → Project agent → DelegateToAssistant tool
                                       ↓
                               PersonalAssistant
                                       ↓
                         FileSystem / Shell / Build / etc.
```

This allows the Telegram bot to handle multi-step engineering workflows without the user needing to interact with individual agents directly.

## Document Ingestion

Uploaded PDF documents are processed for retrieval-augmented generation (RAG):

1. PDF text is extracted using PdfPig
2. Extracted text is chunked and embedded
3. Chunks are stored in Qdrant vector storage
4. When the user asks questions, relevant chunks are retrieved from Qdrant and injected into the agent's system instructions

This enables users to upload reference documents and ask questions about them within the Telegram chat.

## Streaming Responses

Agent responses stream to Telegram in real-time:

- Chunks are buffered and sent via `editMessageText` with 500ms throttling to avoid Telegram rate limits
- Messages exceeding 4000 characters automatically split into continuation messages
- The `editMessageText` calls handle "message is not modified" errors gracefully during streaming

## Event Streams

The `StreamSubscriber` is a `BackgroundService` that subscribes to six Orleans streams:

| Stream | Event Type | Action |
|--------|-----------|--------|
| `notification.sent` | `AgentEvent` | Sends markdown notification to the configured chat |
| `approval.requested` | `AgentEvent` | Renders inline keyboard buttons for user approval |
| `dashboard.changed` | `AgentEvent` | Debounced dashboard markdown update (2s delay) |
| `wizard.started` | `AgentEvent` | Renders wizard step options as inline buttons |
| `orchestration.progress` | `AgentEvent` | Sends real-time orchestration step progress to chat |
| `orchestration.completed` | `AgentEvent` | Sends orchestration completion summary to chat |

All events flow through the Orleans `"agents"` memory stream provider and are published by agents using `Agent.PublishAsync()`.

## Configuration

The bot is configured via the `Telegram` configuration section:

| Setting | Description |
|---------|-------------|
| `BotToken` | Telegram Bot API token from BotFather |
| `WebhookUrl` | Public URL for the webhook (auto-set via ngrok) |
| `WebhookSecretToken` | Optional secret for webhook verification |
| `NgrokApiUrl` | Ngrok local API URL for tunnel discovery |
| `ChatId` | Default chat ID for broadcast notifications (optional) |
