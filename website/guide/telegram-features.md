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

The bot transcribes voice messages using Whisper:

1. User sends a voice message in Telegram
2. Bot downloads the OGG file via the Telegram Bot API
3. `IAudioConverter` converts OGG to WAV
4. `IVoiceTranscriptionService` transcribes the audio
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

## Streaming Responses

Agent responses stream to Telegram in real-time:

- Chunks are buffered and sent via `editMessageText` with 500ms throttling to avoid Telegram rate limits
- Messages exceeding 4000 characters automatically split into continuation messages
- The `editMessageText` calls handle "message is not modified" errors gracefully during streaming

## Event Streams

The `StreamSubscriber` is a `BackgroundService` that subscribes to four Orleans streams:

| Stream | Event Type | Action |
|--------|-----------|--------|
| `notification.sent` | `AgentEvent` | Sends markdown notification to the configured chat |
| `approval.requested` | `AgentEvent` | Renders inline keyboard buttons for user approval |
| `dashboard.changed` | `AgentEvent` | Debounced dashboard markdown update (2s delay) |
| `wizard.started` | `AgentEvent` | Renders wizard step options as inline buttons |

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
