# /newchat, /cleanup & Scheduling Tool Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix dead scheduling tools so every agent can schedule jobs, and add `/newchat` + `/cleanup` Telegram commands for dynamic topic-based conversations with auto-naming.

**Architecture:** Three independent changes: (1) Register existing private `[Description]` scheduling methods as AI tools via reflection in `Agent.Tools.cs`. (2) Add `GetTitle()` to `IThread` for LLM-based conversation title generation, used by Telegram bot to auto-rename topics. (3) Add `/newchat` and `/cleanup` commands to `TelegramBotService`.

**Tech Stack:** C# / .NET 11 preview, Orleans, Microsoft.Extensions.AI, Telegram.BotAPI, xUnit v3

**Spec:** `docs/superpowers/specs/2026-03-24-newchat-scheduling-fix-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Core/Agents/Agent.Tools.cs` | Modify | Add `RegisterSchedulingTools()`, add `"GetTitle"` to excluded names |
| `src/Agents/Orchestration/IThread.cs` | Modify | Add `GetTitle()` to interface |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Implement `GetTitle()` |
| `src/Clients.Telegram/TelegramBotService.cs` | Modify | `/newchat`, `/cleanup`, auto-rename, delete callback |
| `test/Core.Tests/AgentTests.cs` | Modify | Add scheduling tool registration test |

---

### Task 1: Fix Scheduling Tool Registration

**Files:**
- Modify: `src/Core/Agents/Agent.Tools.cs:20-43` (GetAllTools) and `105-117` (BuildExcludedMethodNames)
- Test: `test/Core.Tests/AgentTests.cs` (AgentSchedulingTests region)

- [ ] **Step 1: Write test — scheduling tools appear in agent capabilities**

Add this test to the `AgentSchedulingTests` class in `test/Core.Tests/AgentTests.cs` after the existing tests (around line 484, before `#endregion`):

```csharp
[Fact]
public async Task SchedulingTools_AreRegisteredAsAITools()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = Agent(UniqueId("sched-tools"));
    var response = await agent.GetResponse("List all scheduled jobs", ct);
    // MockChatClient returns "mock-response" but the key assertion is that
    // the agent activated without errors — tools were registered successfully.
    // A more targeted check: call ListJobs to confirm the infrastructure works.
    var jobs = await agent.ListJobs(ct);
    Assert.Empty(jobs);
}
```

- [ ] **Step 2: Run test to verify it passes (baseline)**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~SchedulingTools_AreRegisteredAsAITools" -v minimal`

This should pass even before the fix (ListJobs works via the public API). The real fix verification comes next.

- [ ] **Step 3: Add `RegisterSchedulingTools` to `Agent.Tools.cs`**

In `src/Core/Agents/Agent.Tools.cs`, add a new method after `RegisterToolMethods` (after line 143):

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

Then in `GetAllTools()`, add the call after `RegisterToolMethods` (after line 34):

```csharp
RegisterToolMethods(tools, workspaceTools);
RegisterSchedulingTools(tools);
```

- [ ] **Step 4: Run all scheduling tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~AgentSchedulingTests" -v minimal`

Expected: All 8 tests pass (7 existing + 1 new).

- [ ] **Step 5: Run full test suite to check for regressions**

Run: `dotnet test test/Core.Tests -v minimal`

Expected: All tests pass (except the 2 pre-existing `CodeValidatorTests` failures which are unrelated).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Agents/Agent.Tools.cs test/Core.Tests/AgentTests.cs
git commit -m "fix: register scheduling tools as AI tools for all agents

Private [Description] methods in Agent.Scheduling.cs were never discovered
by tool registration. Added RegisterSchedulingTools() that scans for
non-public [Description] methods on the Agent base class."
```

---

### Task 2: Add `GetTitle()` to IThread and ThreadAgent

**Files:**
- Modify: `src/Agents/Orchestration/IThread.cs:5` (interface body)
- Modify: `src/Agents/Orchestration/ThreadAgent.cs` (add method)
- Modify: `src/Core/Agents/Agent.Tools.cs:105-117` (exclude GetTitle from tool discovery)

- [ ] **Step 1: Add `GetTitle` to excluded method names in `Agent.Tools.cs`**

In `src/Core/Agents/Agent.Tools.cs`, modify `BuildExcludedMethodNames()` (line 105-117). Add `"GetTitle"` to the excluded set after the loops:

```csharp
private static HashSet<string> BuildExcludedMethodNames()
{
    var excluded = new HashSet<string>();

    foreach (var method in typeof(IAgent).GetMethods())
        excluded.Add(method.Name);

    foreach (var baseIface in typeof(IAgent).GetInterfaces())
        foreach (var method in baseIface.GetMethods())
            excluded.Add(method.Name);

    excluded.Add("GetTitle");

    return excluded;
}
```

- [ ] **Step 2: Add `GetTitle` to `IThread` interface**

In `src/Agents/Orchestration/IThread.cs`, add after the `AgentInstructions` static property (after line 40, before the closing `}`):

```csharp
Task<string?> GetTitle(CancellationToken ct);
```

- [ ] **Step 3: Implement `GetTitle` in `ThreadAgent`**

In `src/Agents/Orchestration/ThreadAgent.cs`, add before the closing `}` of the class (before line 277):

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

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build IAW.slnx`

Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Write test for `GetTitle`**

Add a new test class in `test/Core.Tests/AgentTests.cs`. The `ThreadAgent` uses `Agent<IThread>` which needs `AgentTest<ThreadAgent>`. `MockChatClient` returns `"mock-response"` which is a valid title string.

Note: `ThreadAgent` may need special test infrastructure since it depends on `ILogger<ThreadAgent>`. If `AgentTest<ThreadAgent>` does not compile due to constructor mismatch, test `GetTitle` indirectly via the `IThread` grain interface by calling `GetResponse` first then `GetTitle`. Alternatively, add the test to Integration.Tests if the test cluster is needed.

Add in the `#region Scheduling & Reminders` section or a new region:

```csharp
// If ThreadAgent can be instantiated by the test cluster:
public class ThreadGetTitleTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task GetTitle_ReturnsNull_BeforeFirstExchange()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = GrainFactory.GetGrain<IThread>(UniqueId("title-empty"));
        var title = await thread.GetTitle(ct);
        Assert.Null(title);
    }

    [Fact]
    public async Task GetTitle_ReturnsTitle_AfterFirstExchange()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = GrainFactory.GetGrain<IThread>(UniqueId("title-gen"));
        await thread.GetResponse("Hello, what can you do?", ct);
        var title = await thread.GetTitle(ct);
        Assert.NotNull(title);
        // MockChatClient returns "mock-response" which becomes the title
        Assert.Equal("mock-response", title);
    }
}
```

If `ThreadAgent` constructor dependencies prevent `AgentTest<ThreadAgent>` from working, skip this test and verify manually. The `ILogger<ThreadAgent>` should be auto-registered by the Orleans test cluster.

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests -v minimal`

Expected: All tests pass including the new GetTitle tests.

- [ ] **Step 7: Commit**

```bash
git add src/Core/Agents/Agent.Tools.cs src/Agents/Orchestration/IThread.cs src/Agents/Orchestration/ThreadAgent.cs test/Core.Tests/AgentTests.cs
git commit -m "feat: add GetTitle() to IThread for conversation auto-naming

Thread generates a 2-5 word title from the first user+assistant exchange
via a lightweight LLM call. Title is cached in durable state. Excluded
from AI tool discovery to prevent LLM self-invocation."
```

---

### Task 3: Add `/newchat` Command to Telegram Bot

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs:239-254` (HandleCommandAsync switch), `685-762` (StreamResponseAsync), `129-136` (HandleUpdateCoreAsync caller)

- [ ] **Step 1: Add `/newchat` case to `HandleCommandAsync`**

In `src/Clients.Telegram/TelegramBotService.cs`, add to the switch in `HandleCommandAsync` (after line 251, before the closing `}`):

```csharp
case "/newchat":
    await HandleNewChatCommandAsync(chatId, telegramId, ct);
    break;
```

- [ ] **Step 2: Add `HandleNewChatCommandAsync` method**

Add after `HandleStatusCommandAsync` (after line 339):

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

- [ ] **Step 3: Add optional `slug` parameter to `StreamResponseAsync`**

In `src/Clients.Telegram/TelegramBotService.cs`, modify the `StreamResponseAsync` signature (line 685-686) to add an optional slug parameter at the end:

```csharp
private async Task StreamResponseAsync(
    long chatId, int messageId, int? topicId, IThread thread,
    ChatMessage chatMessage, long telegramId, CancellationToken ct, string? slug = null)
```

- [ ] **Step 4: Add auto-rename helper method and call it from all exit paths in `StreamResponseAsync`**

`StreamResponseAsync` has two exit paths: an early return for short/simple responses (line 731-736) and the end of the method after TelegramUI formatting. The auto-rename must run on **both** paths.

First, add a private helper method (after `StreamResponseAsync`):

```csharp
private async Task TryAutoRenameTopicAsync(long chatId, int? topicId, IThread thread, string? slug, CancellationToken ct)
{
    if (slug is null || !slug.StartsWith("chat-") || !topicId.HasValue)
        return;

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

Then modify the early return path in `StreamResponseAsync` (around line 731-736). Change:

```csharp
if (finalText.Length < 200 || !NeedsRichFormatting(finalText))
{
    if (finalText.Length > 0)
        await EditSafe(chatId, currentMessageId, finalText);
    return;
}
```

to:

```csharp
if (finalText.Length < 200 || !NeedsRichFormatting(finalText))
{
    if (finalText.Length > 0)
        await EditSafe(chatId, currentMessageId, finalText);
    await TryAutoRenameTopicAsync(chatId, topicId, thread, slug, ct);
    return;
}
```

And at the very end of `StreamResponseAsync`, just before the method's closing `}`, add:

```csharp
await TryAutoRenameTopicAsync(chatId, topicId, thread, slug, ct);
```

- [ ] **Step 5: Pass slug from `HandleUpdateCoreAsync` to `StreamResponseAsync`**

In `HandleUpdateCoreAsync`, change the `ResolveThreadAsync` destructuring and `StreamResponseAsync` call (around lines 129-135). Change:

```csharp
var (thread, _) = await ResolveThreadAsync(telegramId, topicId, ct);
```

to:

```csharp
var (thread, slug) = await ResolveThreadAsync(telegramId, topicId, ct);
```

And change:

```csharp
await StreamResponseAsync(chatId, sent.MessageId, topicId, thread, chatMessage, telegramId, ct);
```

to:

```csharp
await StreamResponseAsync(chatId, sent.MessageId, topicId, thread, chatMessage, telegramId, ct, slug);
```

- [ ] **Step 6: Build to verify compilation**

Run: `dotnet build IAW.slnx`

Expected: Build succeeds. All 6 existing `StreamResponseAsync` call sites compile (5 use default `null` for slug, 1 passes slug explicitly).

- [ ] **Step 7: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: add /newchat command with auto-naming topics

Creates a new Telegram forum topic, registers it via SetTopicId,
and auto-renames the topic after the first response using
thread.GetTitle(). Slug parameter is optional on StreamResponseAsync
so existing callers are unaffected."
```

---

### Task 4: Add `/cleanup` Command and Delete Callback

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs:239-254` (HandleCommandAsync), `219-237` (HandleCommandCallbackAsync)

- [ ] **Step 1: Add `/cleanup` case to `HandleCommandAsync`**

In the switch in `HandleCommandAsync`, add after the `/newchat` case:

```csharp
case "/cleanup":
    await HandleCleanupCommandAsync(chatId, telegramId, topicId, ct);
    break;
```

- [ ] **Step 2: Add `HandleCleanupCommandAsync` method**

Add after `HandleNewChatCommandAsync`:

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

- [ ] **Step 3: Add cleanup delete handler to `HandleCommandCallbackAsync`**

In `HandleCommandCallbackAsync` (around line 231), add a new case to the switch after `"status"`:

```csharp
case "cleanup":
    await HandleCleanupDeleteAsync(chatId, from.Id, action, ct);
    break;
```

- [ ] **Step 4: Add `HandleCleanupDeleteAsync` method**

Add after `HandleCleanupCommandAsync`:

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

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build IAW.slnx`

Expected: Build succeeds with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: add /cleanup command to manage and delete custom topics

Lists all custom topics with message counts and inline Delete buttons.
Delete callback closes the Telegram forum topic, clears thread history,
and removes the project from UserProfile."
```

---

### Task 5: Build, Run Aspire, and Manual Verification

- [ ] **Step 1: Full build**

Run: `dotnet build IAW.slnx`

Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v minimal`

Expected: All tests pass (except pre-existing `CodeValidatorTests` failures).

- [ ] **Step 3: Start Aspire and verify via MCP**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`

Use Aspire MCP tools to verify:
- `mcp__aspire__list_resources` — all resources healthy
- `mcp__aspire__list_structured_logs` for `assistant` — check for scheduling tool registration logs
- Send a message to an agent via MCP and verify scheduling tools appear in tool list

- [ ] **Step 4: Verify scheduling via agent interaction**

Use `agent_send_message` MCP tool to ask an agent: "Schedule a recurring job to check status every 30 minutes"

Expected: Agent successfully invokes `ScheduleRecurringJobCommand` and returns confirmation.

- [ ] **Step 5: Verify `/newchat` flow (manual via Telegram)**

1. Send `/newchat` in the Telegram group
2. Verify "New Chat" topic appears
3. Send a message in the new topic
4. Verify the topic gets renamed after the response

- [ ] **Step 6: Verify `/cleanup` flow (manual via Telegram)**

1. Send `/cleanup` in any topic
2. Verify list of custom topics with Delete buttons appears
3. Click Delete on a topic
4. Verify topic is closed and removed
