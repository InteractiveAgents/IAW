# Telegram Bot Streaming Responses Design

## Goal

Stream LLM responses to Telegram users in real time using the Bot API 9.3+ `sendMessageDraft` method, replacing the current stub that never returns a response.

## Architecture

TelegramBotGrain gets `[Llm<Claude45Haiku>] IChatClient` injected and calls its own `SendAsync()`. As tokens yield from `IAsyncEnumerable<string>`, they're batched on a 400ms throttle and streamed via `SendMessageDraftAsync`. A final `SendMessageAsync` commits the permanent message.

```
User message -> HandleTextMessage -> SendAsync(message) -> IAsyncEnumerable<string>
  -> every ~400ms: SendMessageDraftAsync(chatId, draftId, accumulatedText)
  -> on completion: SendMessageAsync(chatId, fullText, threadId)
```

## Streaming Method

**Primary:** `sendMessageDraft` (Bot API 9.3+, available in Telegram.BotAPI 9.4.0). Supported for bots with forum topic mode enabled (already the case for this bot). Progressive text with animated transitions, finalized by `sendMessage`.

## Changes

1. **TelegramBotGrain constructor** — Add `[Llm<Claude45Haiku>] IChatClient chatClient`, call `Activate(chatClient)` in `OnActivateAsync`.
2. **New `StreamResponseAsync` method** — Calls `SendAsync(message)`, batches tokens on 400ms timer, calls `SendMessageDraftAsync` per batch, finalizes with `SendMessageAsync`.
3. **`HandleTextMessage` rewrite** — Replace "Message received. Processing..." with streaming flow. Remove PersonalAssistant delegation.
4. **AppHost** — Add `WithLLMEnvironment(builder)` to telegram-bot project for LLM config/API keys.
5. **SystemPrompt** — Override for bot's conversational persona.

## Throttling

- Batch tokens, send draft every ~400ms (2.5 updates/sec)
- Re-send typing indicator every 4 seconds during generation
- On 429 BotRequestException, respect `retry_after` and skip intermediate drafts

## Error Handling

- If `SendMessageDraftAsync` throws, log and continue — final `SendMessageAsync` still delivers complete response
- If `SendAsync` throws (LLM error), send error message to user
- Wrap entire flow in try/catch to prevent grain deactivation

## Files

- Modify: `src/Clients.Telegram.Bot/TelegramBotGrain.cs`
- Modify: `src/IAW.AppHost/AppHost.cs`
- Modify: `src/Clients.Telegram.Bot/TelegramBot.csproj` (if LLM package ref needed)
