# /newchat Topic Management & Scheduling Tool Fix

**Date:** 2026-03-24
**Branch:** feat/self-improving-loop

## Problem Statement

Two issues identified:

1. **Telegram UX gap**: Users cannot create new conversation topics from the bot. The only topics are the 2 hardcoded ones from `/start`. Users want multiple isolated conversations, each with their own context, created on demand.

2. **Scheduling tools are dead code**: `Agent.Scheduling.cs` defines 4 tool commands (`ScheduleJobCommand`, `ScheduleRecurringJobCommand`, `CancelJobCommand`, `ListJobsCommand`) as private methods with `[Description]` attributes. They are never registered as AI tools because:
   - `RegisterToolMethods` (Agent.Tools.cs:34) only scans the `WorkspaceTools` object, not the Agent itself
   - `DiscoverInterfaceTools` only scans the leaf agent interface (e.g., `IThread`), not the `Agent` base class
   - The public `ScheduleJob`/`ListJobs` methods on `IAgent` are explicitly excluded from tool discovery (Agent.Tools.cs:109)
   - The private `*Command` wrapper methods with `[Description]` are never scanned because `GetMethods(Public | Instance | DeclaredOnly)` skips non-public methods

## Design

### Part 1: `/newchat` Command

**Flow:**

1. User sends `/newchat` in the Telegram group
2. `TelegramBotService` creates a new forum topic via `CreateForumTopicAsync` with temp name "New Chat"
3. Stores the mapping via `SetTopicId(slug, topicId)` — this writes to `state.Projects` which `ResolveProject` searches, and `GetTopicId` reads. Single call does both routing and lookup.
4. Sends a welcome nudge in the new topic: "What would you like to work on?"
5. User sends first message — normal `StreamResponseAsync` flow
6. After streaming completes, `TelegramBotService` checks if the topic has a temp name (slug starts with `chat-`)
7. Calls `thread.GetTitle()` — the Thread grain generates a 2-5 word title from the first exchange
8. Renames the topic via `EditForumTopicAsync`

**IThread changes:**

Add `GetTitle` to `IThread`. Note: this method returns `Task<string?>` which is NOT a simple return type per `IsToolSafeReturnType` (nullable string), so `DiscoverInterfaceTools` will auto-register it as an LLM tool. To prevent this, add `"GetTitle"` to `ExcludedMethodNames` in `Agent.Tools.cs`.

```csharp
Task<string?> GetTitle(CancellationToken ct);
```

**ThreadAgent.GetTitle implementation:**

```csharp
public async Task<string?> GetTitle(CancellationToken ct)
{
    if (State.TryGetValue("title", out var entry))
        return entry.Value.ToString();

    if (History.Count < 2)
        return null;

    var firstUser = History.FirstOrDefault(m => m.Role == "user")?.Text;
    var firstAssistant = History.FirstOrDefault(m => m.Role == "assistant")?.Text;
    if (firstUser is null) return null;

    var userSnippet = firstUser[..Math.Min(200, firstUser.Length)];
    var assistantSnippet = firstAssistant?[..Math.Min(200, firstAssistant.Length)] ?? "";
    var prompt = $"Generate a 2-5 word title for this conversation. Reply with ONLY the title, nothing else.\n\nUser: {userSnippet}\nAssistant: {assistantSnippet}";

    var messages = new List<Microsoft.Extensions.AI.ChatMessage>
    {
        new(ChatRole.User, prompt)
    };
    var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
    var title = response.Text?.Trim().Trim('"') ?? "Chat";

    State["title"] = new StateEntry("title", title);
    await WriteStateAsync(ct);
    return title;
}
```

Lightweight LLM call — no tools, no system prompt, just title extraction.

**TelegramBotService — `/newchat` command handler:**

```csharp
case "/newchat":
    await HandleNewChatCommandAsync(chatId, telegramId, ct);
    break;
```

```csharp
private async Task HandleNewChatCommandAsync(long chatId, long telegramId, CancellationToken ct)
{
    var slug = $"chat-{Guid.NewGuid().ToString("N")[..6]}";

    try
    {
        var topic = await botClient.CreateForumTopicAsync(chatId, "New Chat");
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
        await userProfile.SetTopicId(slug, topic.MessageThreadId, ct);
        await botClient.SendMessageAsync(chatId, "What would you like to work on?",
            messageThreadId: topic.MessageThreadId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to create new chat topic");
        await botClient.SendMessageAsync(chatId, "Could not create topic. Make sure the group has Topics enabled.");
    }
}
```

**TelegramBotService — auto-rename after first response:**

`StreamResponseAsync` gets an optional `string? slug = null` parameter. All 6 existing call sites continue to compile (default null = no rename). Only the main message path in `HandleUpdateCoreAsync` passes the slug from `ResolveThreadAsync`.

```csharp
// Updated signature
private async Task StreamResponseAsync(
    long chatId, int messageId, int? topicId, IThread thread,
    ChatMessage chatMessage, long telegramId, CancellationToken ct, string? slug = null)

// At the end, after final edit:
if (slug is not null && slug.StartsWith("chat-") && topicId.HasValue)
{
    try
    {
        var title = await thread.GetTitle(ct);
        if (title is not null)
            await botClient.EditForumTopicAsync(chatId, topicId.Value, name: title);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Best-effort topic rename failed");
    }
}
```

Caller change in `HandleUpdateCoreAsync`:

```csharp
var (thread, slug) = await ResolveThreadAsync(telegramId, topicId, ct);
// ...
await StreamResponseAsync(chatId, sent.MessageId, topicId, thread, chatMessage, telegramId, ct, slug);
```

### Part 2: `/cleanup` Command

**Flow:**

1. User sends `/cleanup`
2. Bot lists all custom topics (skips predefined: general, personal, iaw) with message counts
3. Each topic gets a "Delete" inline button
4. Clicking "Delete" → closes the Telegram forum topic, clears thread history, removes from UserProfile

**Command handler:**

```csharp
case "/cleanup":
    await HandleCleanupCommandAsync(chatId, telegramId, topicId, ct);
    break;
```

```csharp
private async Task HandleCleanupCommandAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
{
    var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
    var projects = await userProfile.GetProjects(ct);

    var sb = new StringBuilder("Your topics:\n\n");
    var buttons = new List<InlineKeyboardButton[]>();

    foreach (var proj in projects)
    {
        if (proj.Slug is "general" or "personal" or "iaw") continue;

        var grainId = $"{telegramId}/{proj.Slug}";
        var thread = clusterClient.GetGrain<IThread>(grainId);
        try
        {
            var history = await thread.GetHistory(ct);
            var title = await thread.GetTitle(ct) ?? proj.Slug;
            sb.AppendLine($"- {title} ({history.Count} messages)");
            buttons.Add([new InlineKeyboardButton($"Delete: {title}")
                { CallbackData = $"cmd:cleanup:{proj.Slug}" }]);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get info for topic {Slug}", proj.Slug);
        }
    }

    if (buttons.Count == 0)
    {
        sb.AppendLine("No custom topics to clean up.");
        await botClient.SendMessageAsync(chatId, sb.ToString(), messageThreadId: topicId);
        return;
    }

    var keyboard = new InlineKeyboardMarkup([.. buttons]);
    await botClient.SendMessageAsync(chatId, sb.ToString(), replyMarkup: keyboard, messageThreadId: topicId);
}
```

**Delete callback handler** — add to `HandleCommandCallbackAsync`:

CallbackData format: `cmd:cleanup:{slug}` — parsed by existing `Split(':', 3)` as `parts[1] = "cleanup"`, `parts[2] = slug`.

```csharp
case "cleanup":
    await HandleCleanupDeleteAsync(chatId, callbackQuery.From.Id, action, ct);
    break;
```

```csharp
private async Task HandleCleanupDeleteAsync(long chatId, long telegramId, string slug, CancellationToken ct)
{
    var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());

    var topicId = await userProfile.GetTopicId(slug, ct);
    if (topicId.HasValue)
    {
        try { await botClient.CloseForumTopicAsync(chatId, topicId.Value); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to close topic {Slug}", slug); }
    }

    var thread = clusterClient.GetGrain<IThread>($"{telegramId}/{slug}");
    await thread.ClearHistory(ct);
    await userProfile.RemoveProject(slug, ct);

    await botClient.SendMessageAsync(chatId, $"Deleted topic: {slug}");
}
```

### Part 3: Scheduling Tool Registration Fix

**Root cause:** The 4 scheduling command methods in `Agent.Scheduling.cs` (lines 189-230) are private with `[Description]` but never discovered by any tool registration path.

**Fix:** Add `RegisterSchedulingTools()` to `Agent.Tools.cs` that discovers private `[Description]` methods on the Agent base class:

```csharp
// In GetAllTools(), after RegisterToolMethods(tools, workspaceTools):
RegisterSchedulingTools(tools);
```

```csharp
private void RegisterSchedulingTools(List<AITool> tools)
{
    var methods = typeof(Agent).GetMethods(
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    foreach (var method in methods)
    {
        if (method.GetCustomAttributes(typeof(DescriptionAttribute), false).Length > 0)
        {
            try
            {
                tools.Add(AIFunctionFactory.Create(method, this));
            }
            catch
            {
                // method signature incompatible — skip silently (matches DiscoverInterfaceTools pattern)
            }
        }
    }
}
```

This discovers all private `[Description]` methods on Agent (the 4 scheduling commands) and registers them as AI tools. Every agent automatically gets scheduling capabilities. No new classes needed — the methods already exist and are well-structured.

### Part 4: Simplify UserProfile Project Registration

`RegisterProject` and `SetTopicId` write to the same `state.Projects` dictionary. `SetTopicId` stores the int as a string — which is exactly what `ResolveProject` reverse-searches. `RegisterProject` is redundant.

**Changes:**
- `/start` handler (line 289): replace `RegisterProject("general", "general")` with `SetTopicId("general", 0)` (or a sentinel) — actually "general" has no Telegram topic ID, so keep this one call as-is since it stores a non-numeric key
- `ResolveThreadAsync` (line 351): replace `RegisterProject(slug, topicKey)` with `SetTopicId(slug, int.Parse(topicKey))` when topicKey is numeric, else keep `RegisterProject` for "general"
- `/newchat`: only calls `SetTopicId` (already simplified above)

Alternatively, keep `RegisterProject` for the "general" edge case (no integer topic ID) and use `SetTopicId` everywhere else. Minimal change, no regressions.

## Files to Modify

| File | Change |
|------|--------|
| `src/Core/Agents/Agent.Tools.cs` | Add `RegisterSchedulingTools()`, add `"GetTitle"` to `ExcludedMethodNames` |
| `src/Agents/Orchestration/IThread.cs` | Add `GetTitle()` method |
| `src/Agents/Orchestration/ThreadAgent.cs` | Implement `GetTitle()` with LLM title generation |
| `src/Clients.Telegram/TelegramBotService.cs` | Add `/newchat`, `/cleanup` commands, auto-rename, delete callback handler, slug parameter on `StreamResponseAsync`, simplify `ResolveThreadAsync` to use `SetTopicId` |

## Files NOT Changed

- `Agent.Scheduling.cs` — the 4 command methods stay as-is, just get discovered now
- `Agent.cs` — no changes needed
- No new files created

## Testing

- Existing `AgentSchedulingTests` (7 tests) validate scheduling infrastructure
- Add test: verify scheduling tools appear in agent tool list
- Add test: `GetTitle()` returns null before first exchange, generates title after
- Manual: `/newchat` creates topic, auto-renames after first message
- Manual: `/cleanup` lists and deletes custom topics
- Manual: verify agents can schedule jobs via natural language
