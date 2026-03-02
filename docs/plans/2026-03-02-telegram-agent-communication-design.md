# Telegram Agent Communication System Design

## Goal

Build a multi-agent communication system where Telegram users interact with a team of 17 specialized IAW agents through semantic routing, voice input, voice calls, and persistent memory -- all orchestrated through Telegram forum topics with real-time streaming.

## Architecture Overview

Tiered routing architecture: Qdrant semantic search for fast agent matching, PersonalAssistant (Sonnet 4.6) fallback for ambiguous/multi-intent messages. Voice pipeline via Foundry Local Whisper (speech-to-text) and PersonaPlex (speech-to-speech for calls). UserAgent as central preference store with notification-based cache sync to domain agents.

## 1. Per-Chat Grain Isolation

Replace `ITelegramBot` / `TelegramBotGrain` with `ITelegramConversation` / `TelegramConversationGrain`. Each Telegram chat gets its own grain instance keyed by `$"conversation-{chatId}"`, providing isolated conversation history, state, and preferences.

The webhook endpoint uses `grains.GetGrain<ITelegramConversation>($"conversation-{chatId}")` instead of a single shared grain.

`ITelegramConversation` handles multiple data types: text, voice messages, voice calls, callbacks, photos, and documents.

## 2. Qdrant Semantic Router

### Infrastructure

Aspire-managed Qdrant container: `builder.AddQdrant("qdrant")`. Client injected via `builder.AddQdrantClient("qdrant")`.

### Agent Registry Collection

On silo startup, embed all 17 agent metadata entries (display name, system prompt, capabilities) into an `"agent-routing"` Qdrant collection. Each vector point has:
- Vector: embedding of `"{DisplayName}: {SystemPrompt} {Capabilities}"`
- Payload: `{ agentId, grainInterface }`

### Routing Flow

1. User message arrives (text or transcribed voice)
2. `IAgentRouter` grain embeds the message using the same embedding model
3. Nearest-neighbor search against `"agent-routing"` collection (top-1)
4. If confidence >= 0.7: route directly to matched agent's `SendAsync`
5. If confidence < 0.7: escalate to PersonalAssistant for LLM-based task decomposition

## 3. Telegram Topic Layout

4 forum topics per chat:

| Topic | Purpose |
|-------|---------|
| **Assistant** | Main conversation. User messages + agent responses streamed via `sendMessageDraft`. Responding agent shown as prefix: `[Weather] 72F sunny` |
| **Team** | Multi-agent collaboration. When PersonalAssistant decomposes a task, each agent's progress streams here via Orleans `agent-events` stream subscription |
| **Notifications** | Alerts from event-driven agents (NuGet outdated, GitHub releases, SelfImprovement proposals) |
| **Settings** | User preferences via UserAgent. Commands like `/preference weather_unit celsius` |

## 4. Voice Pipeline

### A. Voice Messages (Speech-to-Text)

1. TelegramConversation grain downloads OGG Opus file via `bot.GetFileAsync()`
2. `AudioConverter` service: OGG Opus -> WAV using Concentus + NAudio (pure .NET)
3. `VoiceTranscriptionService`: WAV -> text via Foundry Local Whisper (local, free, private)
4. Transcribed text enters the same Qdrant routing pipeline
5. Text response streamed back via `sendMessageDraft`

Aspire: `builder.AddAzureAIFoundry("foundry").RunAsFoundryLocal()`. Telegram bot `WaitFor(foundry)`.

NuGet dependencies: `Microsoft.AI.Foundry.Local`, `Concentus`, `Concentus.OggFile`, `NAudio`.

### B. Voice Calls (Speech-to-Speech via PersonaPlex)

1. Telegram voice call connects to the bot
2. Audio stream fed into PersonaPlex pipeline (ONNX-based, local inference)
3. PersonaPlex handles full-duplex: audio in -> text understanding -> response -> audio out
4. Configurable voice personas (16+ voice embeddings)

NuGet: `ElBruno.PersonaPlex`.

Note: Telegram Bot API voice call support is limited. May require TDLib for full call handling.

## 5. Memory & Preference System

### UserAgent as Source of Truth

UserAgent (already implemented with `SetPreference/GetPreference/AddMemory/GetMemories` tools) stores all user preferences keyed by `user-{telegramUserId}`.

### Preference Change Flow

1. User says "I prefer Celsius" -> detected by responding agent
2. Agent calls `UserAgent.SetPreference("weather_unit", "celsius")`
3. UserAgent publishes notification on topic `"user.preference.changed"` with `{ key, value }`
4. Subscribed agents update their local state cache

### Agent-Local Cache

Each agent subscribes to `"user.preference.changed"` and caches relevant preferences in durable state. On activation, agents fetch relevant preferences from UserAgent.

### LLM-Driven Preference Detection

Agent system prompts instruct them to notice and record user preferences during conversation. When detected, agents proactively call `UserAgent.AddMemory()`.

## 6. Complete Message Flow

```
User sends message in Telegram (text/voice/call)
  |
  +-- Text: TelegramConversation grain receives text directly
  +-- Voice message: Download OGG -> Concentus -> WAV -> Whisper -> text
  +-- Voice call: Audio stream -> PersonaPlex (full-duplex) -> response audio
  |
  v (text path)
  AgentRouter grain (Qdrant semantic search)
  |
  +-- High confidence (>=0.7): Route to specialized agent
  |     -> Agent.SendAsync() -> tokens -> sendMessageDraft in Assistant topic
  |
  +-- Low confidence (<0.7): Escalate to PersonalAssistant
        -> Decomposes task -> assigns to team agents
        -> Each agent's events stream to Team topic
        -> Final response streamed to Assistant topic
  |
  v (async, during conversation)
  Agent detects preference -> UserAgent.SetPreference()
  -> UserAgent publishes notification -> subscribed agents update cache
```

## Files

| Action | File | Purpose |
|--------|------|---------|
| Create | `src/Clients.Telegram.Bot/ITelegramConversation.cs` | New grain interface replacing ITelegramBot |
| Create | `src/Clients.Telegram.Bot/TelegramConversationGrain.cs` | Per-chat conversation grain |
| Create | `src/Core/Routing/IAgentRouter.cs` | Semantic router grain interface |
| Create | `src/Core/Routing/AgentRouterGrain.cs` | Qdrant-based routing |
| Create | `src/Clients.Telegram.Bot/Services/VoiceTranscriptionService.cs` | Foundry Local Whisper |
| Create | `src/Clients.Telegram.Bot/Services/AudioConverter.cs` | OGG Opus -> WAV |
| Create | `src/Clients.Telegram.Bot/Services/VoiceCallService.cs` | PersonaPlex voice calls |
| Modify | `src/Clients.Telegram.Bot/Program.cs` | Register services, update grain ref |
| Modify | `src/IAW.AppHost/AppHost.cs` | Add Qdrant, Foundry Local |
| Modify | `src/IAW.AppHost/IAWExtensions.cs` | Qdrant wiring helpers |
| Modify | `src/Clients.Telegram.Bot/TelegramBot.csproj` | Add NuGet dependencies |

## Tech Stack

- Orleans 10.0 (grains, streaming, journaling)
- Telegram.BotAPI 9.4.0 (sendMessageDraft, voice file download)
- Aspire.Hosting.Qdrant (vector database container)
- Qdrant.Client (semantic search)
- Microsoft.AI.Foundry.Local (Whisper speech-to-text)
- Concentus + NAudio (OGG Opus audio conversion)
- ElBruno.PersonaPlex (speech-to-speech voice calls)
- Microsoft.Extensions.AI (IChatClient, embeddings)

## Implementation Phases

1. Per-chat isolation + ITelegramConversation rename
2. Qdrant infrastructure + AgentRouter grain
3. Wire routing into TelegramConversation (text messages)
4. Topic layout (Assistant, Team, Notifications, Settings)
5. Team topic streaming (Orleans event stream -> Telegram)
6. Voice messages (Foundry Local Whisper)
7. Memory/preference system (UserAgent sync)
8. Voice calls (PersonaPlex)
