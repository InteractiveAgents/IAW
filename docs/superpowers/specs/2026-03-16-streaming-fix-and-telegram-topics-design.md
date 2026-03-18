# Streaming Fix & Telegram Topic System

**Date**: 2026-03-16
**Status**: Approved

## Problem Statement

Two issues with the current Telegram bot experience:

1. **Tool progress is invisible**: When the Project agent delegates work via `DelegateToAssistant`, the `StreamResponseCore` loop in `Agent.cs` only yields `chunk.Text` from `RunStreamingAsync`. During tool execution (which can take minutes for complex tasks), no chunks are emitted — the user sees an initial message like "Sure! Let me kick this off..." and then nothing. Tool results are swallowed silently.

2. **No topic organization**: The bot dumps everything into a single conversation. There's no separation between personal chat, work projects, scheduled jobs, and notifications. The existing `EnsureTopicsAsync` only creates "Assistant" and "Notifications" topics lazily, with no structured mapping to isolated contexts.

---

## Part 1: Channel-Based Tool Progress Streaming

### Approach

Replace the direct `yield return` loop in `StreamResponseCore` with a `Channel<string>` that merges two sources:
- **LLM text chunks** from `RunStreamingAsync`
- **Tool progress** written by long-running tools via `WriteToolProgress()`

This gives end-to-end streaming: User → Project → PersonalAssistant → Sub-agent, every chunk bubbles up to the Telegram bot in real-time.

### Changes

#### `Agent.cs` — Core streaming infrastructure

Add field and helper:
```csharp
private ChannelWriter<string>? _toolProgressWriter;

protected void WriteToolProgress(string text)
{
    _toolProgressWriter?.TryWrite(text);
}
```

Add `using System.Threading.Channels;` import.

Refactor `StreamResponseCore`:
1. Create `Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true })`
2. Set `_toolProgressWriter = channel.Writer`
3. Start producer: `var producerTask = ProduceLlmStreamAsync(fullPrompt, channel.Writer, cancellationToken);`
   - **CRITICAL**: Start as a bare async call, NOT via `Task.Run()`. Both producer and consumer must run on the Orleans grain scheduler. The grain is single-threaded but both tasks yield control via `await`, allowing Orleans to interleave their continuations. `TryWrite` is non-blocking, and `ReadAllAsync` releases the scheduler while awaiting, so the producer makes progress during reader yield gaps.
4. Consumer: `await foreach (var text in channel.Reader.ReadAllAsync(cancellationToken))` — yield each item
5. After reader completes: `await producerTask;` to propagate exceptions
6. Clear `_toolProgressWriter = null` in `finally`

New private method:
```csharp
private async Task ProduceLlmStreamAsync(
    string prompt, ChannelWriter<string> writer, CancellationToken ct)
{
    try
    {
        await foreach (var chunk in _agent!.RunStreamingAsync(
            prompt, _session, cancellationToken: ct))
        {
            if (chunk.Text is { } text)
                writer.TryWrite(text);
        }
    }
    finally
    {
        writer.TryComplete();
    }
}
```

#### `GetResponse` (non-streaming) behavior

`GetResponse` calls `GetResponseStream` internally. After this change, tool progress chunks will be mixed into the final concatenated string. This is **acceptable** — the tool progress IS part of the response content. The LLM's follow-up after tool completion will provide synthesis, and the full text (including progress) is what should be persisted in history.

#### `Project.cs` — `DelegateToAssistant` streams from PA

Change from:
```csharp
var response = await assistant.GetResponse(taskDescription, CancellationToken.None);
return response;
```

To:
```csharp
var sb = new StringBuilder();
WriteToolProgress("\n\n---\nDelegating to engineering team...\n\n");
await foreach (var chunk in assistant.GetResponseStream(taskDescription, CancellationToken.None))
{
    sb.Append(chunk);
    WriteToolProgress(chunk);
}
WriteToolProgress("\n---\n");
return sb.ToString();
```

Add `using System.Text;` import.

**Cancellation note**: The existing code uses `CancellationToken.None` for the sub-agent call. This is a pre-existing limitation — if the user cancels Telegram streaming, sub-agent work continues. Fixing cancellation propagation through the full chain is a follow-up improvement, not part of this spec.

#### `PersonalAssistantAgent.cs` — `AssignTaskToAgent` forwards sub-agent chunks

In the existing `await foreach` loop (lines 126-127), add progress forwarding:

```csharp
WriteToolProgress($"\n[{agentKey}]: ");
try
{
    await foreach (var chunk in agent.GetResponseStream(prompt, ct))
    {
        responseBuilder.Append(chunk);
        WriteToolProgress(chunk);
    }
}
// existing catch block unchanged
```

**`AssignBackgroundTask`**: This method fires work via `Task.Run` and returns immediately. It cannot stream progress because there's no active `_toolProgressWriter` by the time the background task runs. This is by design — background tasks report completion via Orleans stream events, not inline streaming. No changes needed.

#### Multi-silo safety

The `Channel<string>` lives inside the grain instance, which always runs on exactly one silo. Cross-grain `GetResponseStream` calls are proxied by Orleans transparently. No cross-silo channel sharing occurs.

**Pre-existing issue**: The Orleans `MemoryStream` provider is silo-local and won't work multi-silo for pub/sub events. Unrelated to this fix.

#### Shared PA grain limitation

The PersonalAssistant grain ID is hardcoded to `"personal-assistant"` (Project.cs line 102). If multiple users delegate simultaneously, they share the same PA grain. Since Orleans grains are single-threaded, requests are serialized — user B waits for user A's delegation to complete. Tool progress from different users won't leak into each other's streams because each delegation runs sequentially and `_toolProgressWriter` belongs to the calling Project grain, not the PA. This is a pre-existing scalability limitation, not a correctness issue.

---

## Part 2: Telegram Forum Topic System

### Topic Layout

On `/start`, the bot creates these forum topics (if they don't already exist):

| Topic | Color | Slug | Grain ID | Behavior |
|-------|-------|------|----------|----------|
| General | (built-in, always exists) | `general` | `{userId}/general` | Concierge — quick questions, cross-topic awareness, project creation menu |
| Personal | Purple `0xCB86DB` | `personal` | `{userId}/personal` | Personal assistant — memories, preferences, casual |
| IAW | Blue `0x6FB9F0` | `iaw` | `{userId}/iaw` | This project — Aspire monitoring, troubleshooting, builds |
| Scheduled | Green `0x8EEE98` | `scheduled` | `{userId}/scheduled` | Job management — list/create/cancel recurring tasks, pinned dashboard |
| Notifications | Orange `0xFB6F5F` | `notifications` | N/A (routing target only) | System alerts, approvals, task completion events |

**General topic**: Does NOT need `CreateForumTopicAsync` — it always exists in supergroups with topics enabled. Messages with `messageThreadId = null` go to General. The existing code already maps `topicId == null` → `"general"` key.

### Topic ID Persistence

**Problem**: Current `EnsureTopicsAsync` stores topic IDs in volatile instance fields (`_assistantTopicId`, `_notificationsTopicId`). These are lost on restart.

**Fix**: Reuse the existing `UserProfile.Projects` dictionary (`IDurableDictionary<string, string>`) which already maps `slug → topicId` as strings. No new dictionary needed.

Add two helper methods to `IUserProfile` / `UserProfile`:
```csharp
Task<int?> GetTopicId(string slug, CancellationToken ct);
Task SetTopicId(string slug, int topicId, CancellationToken ct);
```

Implementation parses the stored string to int. This avoids the duplication of having both `Projects` and a separate `TopicIds` dictionary.

Remove the volatile fields `_assistantTopicId` and `_notificationsTopicId` from `TelegramBotService`.

### `/start` Command Flow

1. Detect `/start` in `HandleUpdateCoreAsync` before message processing (check `text.StartsWith("/")`)
2. Load existing topic IDs from UserProfile
3. For each predefined topic (Personal, IAW, Scheduled, Notifications):
   a. If UserProfile already has a topicId for this slug → use it
   b. Otherwise, call `CreateForumTopicAsync(chatId, name, iconColor)`
   c. On success → store via `userProfile.SetTopicId(slug, topicId)`
   d. On `TOPIC_NAME_ALREADY_EXISTS` → topic exists but we don't have its ID. The Telegram Bot API has no "list forum topics" endpoint. **Mitigation**: Send a message to the group saying "Topic '{name}' already exists but I don't have its ID. Please send any message in that topic so I can discover it." When a message arrives from that topic, `ResolveProjectAsync` will register the mapping automatically.
4. **General topic**: Send and pin a welcome/control-panel message (no `messageThreadId` — goes to General):

```
Welcome to IAW!

Your Topics:
- General — quick questions, overview
- Personal — personal assistant, memories
- IAW — project monitoring & troubleshooting
- Scheduled — recurring jobs dashboard
- Notifications — system alerts

[+ New Project]  [Status]
```

5. **Scheduled topic**: Send and pin a dashboard message:

```
Active Schedules

- Daily Weather → Personal (every 24h, next: 08:00)

Last updated: 2026-03-16 17:00
```

Store the pinned message ID in UserProfile preferences for later editing.

6. **Default scheduled job**: Create a "Daily Weather" job on the Personal Project grain (interval: 24h, description: "Check current weather and send a brief forecast"). The Personal Project grain delegates to PA, which can use available agents to fulfill weather requests.

7. **Idempotency**: Before creating anything, check UserProfile for existing setup. If topics are already registered, skip creation. Store a `setup-complete` flag in UserProfile preferences.

### Topic-Aware Project Instructions

The `Project` grain adjusts `Instructions` based on slug from `GetPrimaryKeyString()`:

```csharp
protected override string Instructions => GetTopicSlug() switch
{
    "general" => """
        You are the general assistant for this workspace. Answer quick questions directly.
        For complex multi-step work, delegate via DelegateToAssistant.
        You have awareness of all topics — give status updates when asked.
        If a conversation goes deep into a specific domain, suggest the appropriate topic.
        """,
    "personal" => """
        You are the user's personal assistant. Remember preferences, personal facts,
        and casual conversation. Be warm and helpful. Use memories naturally.
        For technical work, suggest using a work topic instead.
        """,
    "iaw" => """
        You are the assistant for the IAW project. You have access to the Aspire agent
        which can check resource health, read logs, traces, and troubleshoot errors.
        When the user reports an issue, check relevant traces and logs before suggesting fixes.
        For builds, tests, and code changes, delegate via DelegateToAssistant.
        """,
    "scheduled" => """
        You manage scheduled jobs and recurring tasks. Help the user create, list,
        and cancel scheduled jobs. Use ScheduleJobTool and CancelJobTool.
        Show the current schedule when asked.
        """,
    _ => """
        You are a project assistant. Help the user manage their project,
        answer questions, and coordinate tasks. Be concise and actionable.
        For tasks requiring file creation, running commands, building code,
        or any multi-step technical work — ALWAYS use DelegateToAssistant.
        """
};

private string GetTopicSlug()
{
    var key = this.GetPrimaryKeyString();
    var slashIndex = key.LastIndexOf('/');
    return slashIndex >= 0 ? key[(slashIndex + 1)..] : key;
}
```

The default case covers custom projects created via "New Project".

### Interactive Project Creation

**Button callback format**: Use 3-part format `cmd:projects:new` to be compatible with UISession's `Split(':', 3)` parser.

**Handling**: Intercept `cmd:` callbacks in `TelegramBotService.HandleCallbackQueryAsync` BEFORE delegating to UISession. This keeps command handling at the bot level, not the session level:

```csharp
if (callbackQuery.Data?.StartsWith("cmd:") == true)
{
    await HandleCommandCallbackAsync(callbackQuery, ct);
    return;
}
// existing UISession delegation below
```

**"New Project" flow:**
1. User taps `[+ New Project]` → `CallbackQuery` with data `"cmd:projects:new"`
2. Bot answers callback, sends "What should the project be called?" in General
3. Sets `HasPendingFreeTextInput` on UISession for the General topicKey
4. User types project name (e.g., "CalcEngine")
5. Bot creates Telegram forum topic "CalcEngine" (blue `0x6FB9F0`)
6. Registers `{userId}/calcengine` via UserProfile
7. Edits the welcome message to include the new topic in the list
8. Sends greeting in the new topic: "CalcEngine project created. What would you like to work on?"

**"Status" button** (`cmd:status:show`): Collects active tasks from all registered projects and shows a summary.

### Commands

| Command | Scope | Action |
|---------|-------|--------|
| `/start` | Any topic | Create predefined topics, pin welcome + dashboard, register defaults |
| `/clear` | Any topic | Call `project.ClearHistory()` on current topic's Project grain, confirm |
| `/status` | Any topic | Cross-topic summary: active tasks, running jobs, recent activity |

Commands are detected in `HandleUpdateCoreAsync` as the first check after extracting text:
```csharp
if (text.StartsWith("/"))
{
    await HandleCommandAsync(chatId, from.Id, topicId, text, ct);
    return;
}
```

### Notification Routing

Update `StreamSubscriber` and `TelegramBotService` to route events to the correct topic:

| Event | Current routing | New routing |
|-------|----------------|-------------|
| `approval.requested` | `projectSlug` → chatId lookup | `projectSlug` → resolve topicId from UserProfile, send to originating topic |
| `notification.sent` | Hardcoded `_notificationsTopicId` | Notifications topic (topicId loaded from UserProfile) |
| `dashboard.changed` | Generic notification | Edit pinned dashboard message in Scheduled topic |
| `orchestration.progress` | Generic notification | Route to originating project's topic |
| `orchestration.completed` | Generic notification | Route to originating project's topic |
| `task.completed` | Not routed to Telegram | Route to originating project's topic |

To route to the correct topic, `SendNotificationAsync` and related methods resolve topicId by:
1. Extract userId from `projectSlug` (before `/`)
2. Call `userProfile.GetTopicId(slug)` to get the Telegram messageThreadId
3. Send to `chatId` with `messageThreadId: topicId`

### Chat History Clearing

Telegram Bot API does NOT notify bots when users clear chat history (it's a client-side operation — messages remain server-side). The bot cannot detect this.

**Alternative**: The `/clear` command explicitly:
1. Calls `project.ClearHistory()` on the current topic's Project grain
2. Clears the durable history list and creates a new LLM session
3. Sends a confirmation message: "Conversation cleared."

---

## Files Changed

### Part 1 (Streaming Fix)

| File | Change |
|------|--------|
| `src/Core/Agents/Agent.cs` | Add `_toolProgressWriter` field, `WriteToolProgress()`, refactor `StreamResponseCore` to Channel pattern, add `ProduceLlmStreamAsync`. Add `using System.Threading.Channels;` |
| `src/Agents/Projects/Project.cs` | `DelegateToAssistant` → call `GetResponseStream` + `WriteToolProgress`. Add `using System.Text;` |
| `src/Agents/Orchestration/PersonalAssistantAgent.cs` | `AssignTaskToAgent` → forward chunks via `WriteToolProgress` |

### Part 2 (Topic System)

| File | Change |
|------|--------|
| `src/Clients.Telegram/TelegramBotService.cs` | Replace `EnsureTopicsAsync` with topic setup flow. Add command parsing (`/start`, `/clear`, `/status`). Add `HandleCommandAsync`, `HandleCommandCallbackAsync`. Add `cmd:` callback interception in `HandleCallbackQueryAsync`. Topic-aware message sending. Remove volatile `_assistantTopicId`, `_notificationsTopicId` fields. |
| `src/Clients.Telegram/StreamSubscriber.cs` | Topic-aware notification routing — resolve topicId from UserProfile |
| `src/Core/Contracts/IUserProfile.cs` | Add `GetTopicId(slug)`, `SetTopicId(slug, topicId)` methods |
| `src/Agents/UserProfile/UserProfile.cs` | Implement `GetTopicId` (parse existing Projects dict), `SetTopicId` |
| `src/Agents/Projects/Project.cs` | Topic-aware `Instructions` override with `GetTopicSlug()` helper |

### No changes needed

- `UserProfileDurableState.cs` — reuse existing `Projects` dictionary
- `TelegramBotOptions.cs` — `ChatId` is sufficient for the supergroup ID
- `WebhookSetupService.cs` — webhook setup unchanged
- `StreamResponseAsync` in TelegramBotService — unchanged, it already consumes `IAsyncEnumerable<string>` which now includes tool progress automatically

---

## Testing

### Automated Tests

1. **Streaming + tool progress test** (in `IAW.Testing`): Create an agent with a custom tool that calls `WriteToolProgress("progress")`. Verify that `GetResponseStream` output contains both the LLM text and the progress text. Use `MockChatClient` with tool-calling behavior.

2. **Topic-aware Instructions test**: Create Project grains with different slug patterns (`{id}/general`, `{id}/personal`, `{id}/iaw`, `{id}/work`). Verify each has the correct Instructions content.

3. **`/start` idempotency test**: Call setup twice. Verify topics aren't duplicated and UserProfile state is consistent.

### Manual Tests

- Send a complex task via Telegram → verify streaming shows sub-agent progress in real-time
- `/start` in supergroup → verify all topics created with correct colors
- `/clear` → verify conversation resets
- Send message in each topic → verify isolated contexts (Personal doesn't see Work history)
- Tap `[+ New Project]` → verify topic creation flow
- Verify notification routing to correct topics
- Verify Scheduled topic pinned dashboard updates when jobs change
- Verify multi-message splitting still works when tool progress pushes buffer past 4000 chars

---

## Known Limitations

1. **Telegram has no "list forum topics" API**: If a topic already exists but the bot doesn't have its ID stored, it cannot discover it programmatically. Mitigation: ask user to send a message in the topic for auto-discovery.

2. **Shared PA grain**: All users share `personal-assistant` grain. Requests are serialized. Scalability limitation for multi-user scenarios.

3. **No cancellation propagation**: Cancelling Telegram streaming doesn't cancel sub-agent work. Follow-up improvement.

4. **Memory stream provider is silo-local**: Orleans pub/sub events won't work across silos. Pre-existing issue, requires persistent stream provider for multi-silo.
