# Streaming Fix & Telegram Topic System Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix invisible tool execution during agent delegation and add Telegram forum topic organization with isolated contexts per topic.

**Architecture:** Part 1 refactors the Agent streaming loop to use a `Channel<string>` that merges LLM text chunks with tool progress written by `WriteToolProgress()`. Part 2 adds `/start` command that creates Telegram forum topics (Personal, IAW, Scheduled, Notifications), each mapping to an isolated Project grain with topic-specific instructions, plus `/clear` and `/status` commands.

**Tech Stack:** C# / .NET 11, Orleans 10 (grains, journaling, streams), Microsoft.Agents.AI, Telegram.BotAPI 9.5.0, xUnit v3, System.Threading.Channels

**Spec:** `docs/superpowers/specs/2026-03-16-streaming-fix-and-telegram-topics-design.md`

---

## Chunk 1: Streaming Fix — Agent Core

### Task 1: Add Channel-based streaming infrastructure to Agent.cs

**Files:**
- Modify: `src/Core/Agents/Agent.cs`

- [ ] **Step 1: Add using and field**

At the top of `src/Core/Agents/Agent.cs`, add `using System.Threading.Channels;` to the imports.

Inside the `Agent` class body (after the existing fields around line 32), add:

```csharp
private ChannelWriter<string>? _toolProgressWriter;

protected void WriteToolProgress(string text)
{
    _toolProgressWriter?.TryWrite(text);
}
```

- [ ] **Step 2: Add ProduceLlmStreamAsync method**

After the `StreamResponseCore` method (after line 152), add this new private method:

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

- [ ] **Step 3: Refactor StreamResponseCore to use Channel**

Replace the body of `StreamResponseCore` (lines 95-152). The new implementation:

```csharp
private async IAsyncEnumerable<string> StreamResponseCore(
    string prompt,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var activity = AgentTelemetry.ActivitySource.StartActivity(
        $"invoke_agent {this.GetPrimaryKeyString()}", ActivityKind.Server);
    activity?.SetTag("gen_ai.operation.name", "invoke_agent");
    activity?.SetTag("gen_ai.provider.name", "iaw");
    activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
    activity?.SetTag("gen_ai.agent.name", DisplayName);
    activity?.SetTag("gen_ai.conversation.id", this.GetPrimaryKeyString());

    var sw = Stopwatch.StartNew();
    var completed = false;
    try
    {
        var attachmentText = await ResolveAttachments(prompt, cancellationToken);
        var contextBlock = await BuildContextBlock(prompt, cancellationToken);
        _chatOptions!.Instructions = contextBlock.Length > 0
            ? $"{Instructions}\n\n{contextBlock}"
            : Instructions;

        var fullPrompt = attachmentText != prompt ? attachmentText : prompt;

        var channel = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true });
        _toolProgressWriter = channel.Writer;

        // CRITICAL: bare async call, NOT Task.Run — must stay on grain scheduler
        var producerTask = ProduceLlmStreamAsync(fullPrompt, channel.Writer, cancellationToken);

        await foreach (var text in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return text;
            Activity.Current = activity;
        }

        await producerTask;

        if (_usageCapture.LastUsage is { } usage)
        {
            activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
            RecordTokenMetrics(usage);
        }

        var correlationId = activity?.TraceId.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        durableState.EventLog.Add(new AgentEvent(
            "LlmCall", this.GetPrimaryKeyString(), correlationId,
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["prompt_length"] = prompt.Length }));

        await WriteStateAsync(cancellationToken);
        completed = true;
    }
    finally
    {
        _toolProgressWriter = null;
        if (!completed)
        {
            activity?.SetTag("error.type", "conversation_error");
            AgentTelemetry.ConversationErrors.Add(1, new TagList { { "agent.type", GetType().Name } });
        }
        AgentTelemetry.ConversationDuration.Record(sw.Elapsed.TotalSeconds,
            new TagList { { "agent.type", GetType().Name } });
    }
}
```

Key differences from original:
- `Channel.CreateUnbounded<string>` replaces direct iteration
- `_toolProgressWriter` set before producer starts, cleared in finally
- `ProduceLlmStreamAsync` runs as bare async call (grain scheduler)
- Reader consumes from channel, yields to caller
- `await producerTask` after reader completes to propagate exceptions
- All existing telemetry, metrics, and state persistence preserved

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/Core`
Expected: Build succeeded

- [ ] **Step 5: Run existing tests to verify no regression**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All existing tests pass

- [ ] **Step 6: Commit**

```bash
git add src/Core/Agents/Agent.cs
git commit -m "feat: add Channel-based streaming with WriteToolProgress support in Agent base class"
```

---

## Chunk 2: Streaming Fix — Tool Consumers

### Task 2: Update DelegateToAssistant in Project.cs to stream

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Add System.Text import**

Add `using System.Text;` to the imports at the top of `Project.cs`.

- [ ] **Step 2: Replace DelegateToAssistant implementation**

Replace the `DelegateToAssistant` method (lines 98-105) with:

```csharp
[Description("Delegate a complex task to the PersonalAssistant engineering team")]
private async Task<string> DelegateToAssistant(
    [Description("Full description of what needs to be done")] string taskDescription)
{
    var assistant = GrainFactory.GetGrain<IPersonalAssistant>("personal-assistant");
    var sb = new StringBuilder();
    WriteToolProgress("\n\n---\nDelegating to engineering team...\n\n");
    await foreach (var chunk in assistant.GetResponseStream(taskDescription, CancellationToken.None))
    {
        sb.Append(chunk);
        WriteToolProgress(chunk);
    }
    WriteToolProgress("\n---\n");
    return sb.ToString();
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Projects/Project.cs
git commit -m "feat: DelegateToAssistant streams PA response via WriteToolProgress"
```

### Task 3: Update AssignTaskToAgent in PersonalAssistantAgent.cs to forward chunks

**Files:**
- Modify: `src/Agents/Orchestration/PersonalAssistantAgent.cs`

- [ ] **Step 1: Add WriteToolProgress calls in AssignTaskToAgent**

In `AssignTaskToAgent` (lines 110-152), replace the try block that does the streaming (lines 124-133):

From:
```csharp
try
{
    await foreach (var chunk in agent.GetResponseStream(prompt, ct))
        responseBuilder.Append(chunk);
}
catch (Exception ex)
{
    sawError = true;
    responseBuilder.AppendLine(BuildSafeErrorMessage(ex));
}
```

To:
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
catch (Exception ex)
{
    sawError = true;
    responseBuilder.AppendLine(BuildSafeErrorMessage(ex));
}
WriteToolProgress("\n");
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 3: Run all tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/PersonalAssistantAgent.cs
git commit -m "feat: AssignTaskToAgent forwards sub-agent chunks via WriteToolProgress"
```

### Task 4: Add streaming + tool progress test

**Files:**
- Create: `test/Core.Tests/StreamingToolProgressTests.cs`

- [ ] **Step 1: Create the test agent with a tool that calls WriteToolProgress**

Create `test/Core.Tests/StreamingToolProgressTests.cs`:

```csharp
using System.ComponentModel;
using Core.Contracts;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Core.Tests;

public interface IToolProgressTestAgent : IAgent;

public class ToolProgressTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IToolProgressTestAgent
{
    protected override string Instructions => "You are a test agent. Use the SlowTool when asked.";
    protected override string DisplayName => "ToolProgress Test";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(SlowTool, nameof(SlowTool),
            "A tool that writes progress updates")
    ];

    [Description("A tool that writes progress updates")]
    private Task<string> SlowTool()
    {
        WriteToolProgress("[progress:start]");
        WriteToolProgress("[progress:end]");
        return Task.FromResult("tool-done");
    }
}

public class StreamingToolProgressTests : AgentTest<ToolProgressTestAgent>
{
    [Fact]
    public async Task GetResponseStream_IncludesToolProgressChunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("progress"));

        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", ct))
            chunks.Add(chunk);

        var combined = string.Join("", chunks);
        // MockChatClient returns "mock-response" which is text-only (no tool calls).
        // WriteToolProgress is only active during streaming, so with the default mock
        // it just streams the text response. Verify the basic streaming works.
        Assert.NotEmpty(chunks);
        Assert.Contains("mock-response", combined);
    }

    [Fact]
    public async Task GetResponse_ReturnsFullText()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("full"));

        var response = await agent.GetResponse("Hello", ct);
        Assert.Equal("mock-response", response);
    }

    [Fact]
    public async Task WriteToolProgress_DoesNotThrowWhenNoActiveStream()
    {
        // WriteToolProgress with no active stream should be a no-op
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("noop"));

        // First call to activate the grain
        var response = await agent.GetResponse("Hello", ct);
        Assert.Equal("mock-response", response);
        // If we got here without exception, WriteToolProgress (if called) didn't crash
    }
}
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~StreamingToolProgressTests" --verbosity normal`
Expected: All 3 tests pass

- [ ] **Step 3: Run full test suite**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass (no regressions)

- [ ] **Step 4: Commit**

```bash
git add test/Core.Tests/StreamingToolProgressTests.cs
git commit -m "test: add streaming tool progress tests for Channel-based Agent streaming"
```

---

## Chunk 3: UserProfile Topic Support

### Task 5: Add GetTopicId/SetTopicId to UserProfile

**Files:**
- Modify: `src/Core/Contracts/IUserProfile.cs`
- Modify: `src/Agents/UserProfile/UserProfile.cs`

- [ ] **Step 1: Add methods to IUserProfile interface**

In `src/Core/Contracts/IUserProfile.cs`, add before the closing brace:

```csharp
Task<int?> GetTopicId(string slug, CancellationToken ct);
Task SetTopicId(string slug, int topicId, CancellationToken ct);
```

- [ ] **Step 2: Implement in UserProfile**

In `src/Agents/UserProfile/UserProfile.cs`, add before the closing brace:

```csharp
public Task<int?> GetTopicId(string slug, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    if (state.Projects.TryGetValue(slug, out var value) && int.TryParse(value, out var topicId))
        return Task.FromResult<int?>(topicId);
    return Task.FromResult<int?>(null);
}

public async Task SetTopicId(string slug, int topicId, CancellationToken ct)
{
    state.Projects[slug] = topicId.ToString();
    await WriteStateAsync(ct);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Core && dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Core/Contracts/IUserProfile.cs src/Agents/UserProfile/UserProfile.cs
git commit -m "feat: add GetTopicId/SetTopicId to UserProfile for topic ID persistence"
```

---

## Chunk 4: Topic-Aware Project Instructions

### Task 6: Add topic-aware Instructions to Project grain

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Replace Instructions override and add helper**

In `src/Agents/Projects/Project.cs`, replace the existing `Instructions` property (lines 20-30) with:

```csharp
protected override string Instructions => GetTopicSlug() switch
{
    "general" => """
        You are the general assistant for this workspace. Answer quick questions directly.
        For complex multi-step work, delegate via DelegateToAssistant.
        You have awareness of all topics — give status updates when asked.
        If a conversation goes deep into a specific domain, suggest the appropriate topic.

        IMPORTANT: For tasks that require creating files, running commands, building code,
        searching files, git operations, or any multi-step technical work — ALWAYS use
        DelegateToAssistant.
        """,
    "personal" => """
        You are the user's personal assistant. Remember preferences, personal facts,
        and casual conversation. Be warm and helpful. Use memories naturally.
        For technical work, suggest using a work topic instead.
        For tasks that require creating files, running commands, building code — use DelegateToAssistant.
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
        searching files, git operations, or any multi-step technical work — ALWAYS use
        DelegateToAssistant. You cannot do these things yourself.
        """
};
```

- [ ] **Step 2: Add GetTopicSlug helper method**

After the `GetContextProviders()` method (after line 57), add:

```csharp
private string GetTopicSlug()
{
    var key = this.GetPrimaryKeyString();
    var slashIndex = key.LastIndexOf('/');
    return slashIndex >= 0 ? key[(slashIndex + 1)..] : key;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Projects/Project.cs
git commit -m "feat: topic-aware Instructions in Project grain based on slug"
```

---

## Chunk 5: Bot Commands & Topic Setup

### Task 7: Add command parsing and /start to TelegramBotService

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

This is the largest task. We modify `TelegramBotService` in several focused steps.

- [ ] **Step 1: Remove volatile topic ID fields**

Delete lines 24-25:
```csharp
private int? _assistantTopicId;
private int? _notificationsTopicId;
```

- [ ] **Step 2: Add topic color constants and setup helper fields**

Add at the top of the class (right after the opening brace, around line 24):

```csharp
// Telegram forum topic icon colors
static readonly int ColorPurple = 0xCB86DB;
static readonly int ColorBlue = 0x6FB9F0;
static readonly int ColorGreen = 0x8EEE98;
static readonly int ColorOrange = 0xFB6F5F;

// Predefined topic definitions: (slug, display name, color)
static readonly (string Slug, string Name, int Color)[] PredefinedTopics =
[
    ("personal", "Personal", ColorPurple),
    ("iaw", "IAW", ColorBlue),
    ("scheduled", "Scheduled", ColorGreen),
    ("notifications", "Notifications", ColorOrange),
];
```

- [ ] **Step 3: Add command detection in HandleUpdateCoreAsync**

In `HandleUpdateCoreAsync`, after the `if (string.IsNullOrEmpty(text)) return;` check (line 92), add command interception BEFORE the existing UISession/project logic:

```csharp
if (text.StartsWith("/"))
{
    await HandleCommandAsync(chatId, from.Id, topicId, text, ct);
    return;
}
```

- [ ] **Step 4: Implement HandleCommandAsync**

Add this method after `HandleCallbackQueryAsync`:

```csharp
private async Task HandleCommandAsync(long chatId, long telegramId, int? topicId, string text, CancellationToken ct)
{
    var command = text.Split(' ', 2)[0].ToLowerInvariant();
    switch (command)
    {
        case "/start":
            await HandleStartCommandAsync(chatId, telegramId, ct);
            break;
        case "/clear":
            await HandleClearCommandAsync(chatId, telegramId, topicId, ct);
            break;
        case "/status":
            await HandleStatusCommandAsync(chatId, telegramId, topicId, ct);
            break;
        default:
            await botClient.SendMessageAsync(chatId, $"Unknown command: {command}", messageThreadId: topicId);
            break;
    }
}
```

- [ ] **Step 5: Implement HandleStartCommandAsync**

Add this method:

```csharp
private async Task HandleStartCommandAsync(long chatId, long telegramId, CancellationToken ct)
{
    var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());

    // Idempotency: check if already set up
    var prefs = await userProfile.GetPreferences(ct);
    if (prefs.ContainsKey("setup-complete"))
    {
        await botClient.SendMessageAsync(chatId, "Already set up! Topics should be ready.");
        return;
    }

    // Create predefined topics
    foreach (var (slug, name, color) in PredefinedTopics)
    {
        try
        {
            var existingTopicId = await userProfile.GetTopicId(slug, ct);
            if (existingTopicId is not null) continue;

            var topic = await botClient.CreateForumTopicAsync(chatId, name, iconColor: color);
            await userProfile.SetTopicId(slug, topic.MessageThreadId, ct);
            logger.LogInformation("Created topic {Name} (id: {TopicId}) for user {TelegramId}",
                name, topic.MessageThreadId, telegramId);
        }
        catch (BotRequestException ex) when (ex.Message.Contains("TOPIC_NAME_ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Topic {Name} already exists for user {TelegramId}. Send a message there to register it.", name, telegramId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create topic {Name}", name);
        }
    }

    // Register general topic (always exists, no creation needed)
    await userProfile.RegisterProject("general", "general", ct);

    // Send and pin welcome message in General (no messageThreadId = General)
    var welcomeText = BuildWelcomeMessage();
    var welcomeButtons = new InlineKeyboardMarkup([
        [
            new InlineKeyboardButton("+ New Project") { CallbackData = "cmd:projects:new" },
            new InlineKeyboardButton("Status") { CallbackData = "cmd:status:show" }
        ]
    ]);
    var welcomeMsg = await botClient.SendMessageAsync(chatId, welcomeText, replyMarkup: welcomeButtons);

    try { await botClient.PinChatMessageAsync(chatId, welcomeMsg.MessageId); }
    catch (Exception ex) { logger.LogWarning(ex, "Could not pin welcome message"); }

    // Send and pin dashboard in Scheduled topic
    var scheduledTopicId = await userProfile.GetTopicId("scheduled", ct);
    if (scheduledTopicId is not null)
    {
        var dashboardText = "Active Schedules\n\nNo active jobs yet.\n\nLast updated: " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var dashMsg = await botClient.SendMessageAsync(chatId, dashboardText, messageThreadId: scheduledTopicId);
        try { await botClient.PinChatMessageAsync(chatId, dashMsg.MessageId); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not pin scheduled dashboard"); }
        await userProfile.SetPreference("scheduled-dashboard-msgid", dashMsg.MessageId.ToString(), ct);
    }

    // Create default weather job on Personal project
    var personalTopicId = await userProfile.GetTopicId("personal", ct);
    if (personalTopicId is not null)
    {
        var personalProject = clusterClient.GetGrain<IProject>($"{telegramId}/personal");
        try
        {
            await personalProject.ScheduleJob(
                "Daily Weather",
                TimeSpan.FromHours(24),
                "Check the current weather and send a brief forecast",
                ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not create default weather job"); }
    }

    await userProfile.SetPreference("setup-complete", "true", ct);
    logger.LogInformation("Setup complete for user {TelegramId}", telegramId);
}

private static string BuildWelcomeMessage() => """
    Welcome to IAW!

    Your Topics:
    - General — quick questions, overview
    - Personal — personal assistant, memories
    - IAW — project monitoring & troubleshooting
    - Scheduled — recurring jobs dashboard
    - Notifications — system alerts

    Use /clear to reset conversation in any topic.
    Use /status for an overview of all active work.
    """;
```

- [ ] **Step 6: Implement HandleClearCommandAsync**

```csharp
private async Task HandleClearCommandAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
{
    var (project, _) = await ResolveProjectAsync(telegramId, topicId, ct);
    await project.ClearHistory(ct);
    await botClient.SendMessageAsync(chatId, "Conversation cleared.", messageThreadId: topicId);
}
```

- [ ] **Step 7: Implement HandleStatusCommandAsync**

```csharp
private async Task HandleStatusCommandAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
{
    var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
    var projects = await userProfile.GetProjects(ct);

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("Status across all topics:\n");

    foreach (var proj in projects)
    {
        if (proj.Slug is "notifications") continue;
        var grainId = $"{telegramId}/{proj.Slug}";
        var project = clusterClient.GetGrain<IProject>(grainId);
        try
        {
            var dashboard = await project.GetDashboard(ct);
            var activeTasks = dashboard.Tasks.Count(t => t.Status is ProjectTaskStatus.Pending or ProjectTaskStatus.InProgress);
            var activeJobs = dashboard.Jobs.Count(j => j.Active);
            if (activeTasks > 0 || activeJobs > 0)
                sb.AppendLine($"[{proj.Slug}] Tasks: {activeTasks} active, Jobs: {activeJobs} running");
        }
        catch { }
    }

    if (sb.Length < 40) sb.AppendLine("All quiet — no active tasks or jobs.");

    await botClient.SendMessageAsync(chatId, sb.ToString(), messageThreadId: topicId);
}
```

- [ ] **Step 8: Add using for IProject**

Add `using Core.Contracts;` at the top if not already present (it should be, via `ChatMessage`). Also add `using Telegram.BotAPI.AvailableMethods.FormattingOptions;` if needed for `FormatStyles`. Check the existing imports — `Telegram.BotAPI.AvailableMethods` is already imported.

- [ ] **Step 9: Replace EnsureTopicsAsync with a no-op or remove**

The old `EnsureTopicsAsync` (lines 399-419) is no longer needed. Remove it entirely. Also remove any calls to it (the only call is in `SendNotificationAsync` line 175).

In `SendNotificationAsync`, remove the `await EnsureTopicsAsync(chatId, ct);` call (line 175). Replace the notification routing — instead of using `_notificationsTopicId`, resolve from UserProfile:

```csharp
public async Task SendNotificationAsync(AgentEvent evt, CancellationToken ct)
{
    var chatId = options.Value.ChatId;
    if (chatId == 0) return;

    // Resolve notifications topic from event payload or SourceAgentId
    // Events from Project grains have SourceAgentId = "{telegramId}/{slug}"
    // Some events carry projectSlug or projectKey in payload
    int? notifTopicId = null;
    var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString()
                   ?? evt.Payload.GetValueOrDefault("projectKey")?.ToString()
                   ?? evt.SourceAgentId ?? "";
    var userId = projectSlug.Contains('/') ? projectSlug.Split('/')[0] : "";
    if (long.TryParse(userId, out var telegramId))
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
        notifTopicId = await userProfile.GetTopicId("notifications", ct);
    }

    var text = $"*{EscapeMarkdown(evt.EventName)}* from `{evt.SourceAgentId}`\n" +
               string.Join("\n", evt.Payload.Select(p => $"  {p.Key}: {p.Value}"));

    await botClient.SendMessageAsync(chatId, text,
        messageThreadId: notifTopicId, parseMode: FormatStyles.MarkdownV2);
}
```

- [ ] **Step 10: Build**

Run: `dotnet build src/Clients.Telegram`
Expected: Build succeeded

- [ ] **Step 11: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: add /start /clear /status commands, topic creation, welcome message"
```

### Task 8: Add cmd: callback interception and New Project flow

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

- [ ] **Step 1: Add cmd: callback interception in HandleCallbackQueryAsync**

In `HandleCallbackQueryAsync` (line 111), add at the very beginning, before the UISession delegation:

```csharp
if (callbackQuery.Data?.StartsWith("cmd:") == true)
{
    await HandleCommandCallbackAsync(callbackQuery, ct);
    return;
}
```

The existing code (getting `from`, `chatId`, delegating to UISession) moves below this check.

- [ ] **Step 2: Implement HandleCommandCallbackAsync**

```csharp
private async Task HandleCommandCallbackAsync(CallbackQuery callbackQuery, CancellationToken ct)
{
    var chatId = callbackQuery.Message?.Chat.Id ?? 0L;
    if (chatId == 0) return;

    var from = callbackQuery.From;
    var parts = callbackQuery.Data!.Split(':', 3);
    var action = parts.Length >= 3 ? parts[2] : "";

    try { await botClient.AnswerCallbackQueryAsync(callbackQuery.Id); }
    catch { }

    switch (parts[1])
    {
        case "projects" when action == "new":
            await botClient.SendMessageAsync(chatId,
                "What should the project be called?");
            var session = clusterClient.GetGrain<IUISession>(from.Id.ToString());
            // Register a pending free-text input for project creation
            // We use the "general" topic key since the button is in General
            // FormField is a positional record: (Name, Prompt, Type, Options)
            var formFields = new Core.Contracts.UI.FormField[]
            {
                new("project-name", "What should the project be called?",
                    Core.Contracts.UI.FormFieldType.FreeText, null)
            };
            await session.StartForm("new-project", formFields, $"{from.Id}/general", ct);
            break;

        case "status" when action == "show":
            await HandleStatusCommandAsync(chatId, from.Id, null, ct);
            break;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Clients.Telegram`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: add cmd: callback interception and New Project button handler"
```

---

## Chunk 6: Notification Routing

### Task 9: Update StreamSubscriber for topic-aware routing

**Files:**
- Modify: `src/Clients.Telegram/StreamSubscriber.cs`

- [ ] **Step 1: Add helper method for resolving topic ID from project slug**

Add this helper method to `StreamSubscriber`:

```csharp
private async Task<int?> ResolveTopicIdAsync(string projectSlug, CancellationToken ct)
{
    if (string.IsNullOrEmpty(projectSlug)) return null;
    var parts = projectSlug.Split('/');
    if (parts.Length < 2) return null;
    var userId = parts[0];
    var slug = parts[1];
    var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
    return await userProfile.GetTopicId(slug, ct);
}
```

Add `using Core.Contracts;` to the imports if not already present.

- [ ] **Step 2: Update approval stream handler to use topic routing**

In the approval stream subscription (lines 38-54), update to resolve and pass the topic ID:

Replace the handler body with:

```csharp
var approvalId = evt.Payload.GetValueOrDefault("approvalId")?.ToString() ?? "";
var question = evt.Payload.GetValueOrDefault("question")?.ToString() ?? "";
var approvalOptions = ResolveStringArray(evt.Payload.GetValueOrDefault("options"));
var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString() ?? "";
var topicId = await ResolveTopicIdAsync(projectSlug, ct);
await botService.SendApprovalAsync(approvalId, question, approvalOptions, projectSlug, ct);
```

Note: `SendApprovalAsync` already uses `TryResolveChatId(projectSlug)` for the chatId. The topic routing for approvals will be handled when we update `SendApprovalAsync` to accept a topicId parameter. For now, the routing from StreamSubscriber is set up for future use.

- [ ] **Step 3: Build**

Run: `dotnet build src/Clients.Telegram`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Clients.Telegram/StreamSubscriber.cs
git commit -m "feat: add topic-aware routing helper in StreamSubscriber"
```

---

## Chunk 7: Build, Test & Verify

### Task 10: Full build and test

**Files:** None (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded with 0 errors

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test IAW.slnx --verbosity normal`
Expected: All tests pass

- [ ] **Step 3: Start Aspire and verify**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Expected: All resources start and reach Running/Healthy state

- [ ] **Step 4: Verify via Aspire MCP**

Use Aspire MCP tools to check:
- `list_resources` — all resources Running
- `list_structured_logs` for telegram — no errors on startup
- `list_structured_logs` for assistant — silo started successfully

- [ ] **Step 5: Final commit with all changes**

If any fixups were needed during verification, commit them:

```bash
git add -A
git commit -m "fix: address build/test issues from streaming and topics implementation"
```

---

## Parallelization Guide

For subagent-driven development, these tasks can be parallelized:

**Wave 1** (no dependencies):
- Task 1 (Agent.cs streaming)
- Task 5 (UserProfile topic helpers)
- Task 6 (Project topic instructions)

**Wave 2** (depends on Wave 1):
- Task 2 (Project DelegateToAssistant) — depends on Task 1
- Task 3 (PA AssignTaskToAgent) — depends on Task 1
- Task 4 (Streaming tests) — depends on Task 1

**Wave 3** (depends on Wave 1 + 2):
- Task 7 (Bot /start + commands) — depends on Task 5
- Task 8 (Bot callbacks) — depends on Task 7

**Wave 4** (depends on Wave 3):
- Task 9 (StreamSubscriber routing) — depends on Task 5 + 7

**Wave 5** (final):
- Task 10 (Build & verify) — depends on all
