# Telegram UX Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unified Telegram message delivery with live progress updates, structured orchestration results, follow-up buttons on job results, and verbatim user message preservation.

**Architecture:** Two delivery paths — synchronous streaming (already works, minor tuning) and asynchronous event delivery (progress updates via `OrchestrationProgress` events → in-place message edits, job results via `OrchestrationResult` → TelegramUIAgent → RichOutput buttons). CodeOrchestrator publishes phase events; StreamSubscriber routes them to TelegramBotService which tracks per-task progress messages in a `ConcurrentDictionary`.

**Tech Stack:** C# / .NET 11, Orleans 9, Telegram Bot API, xunit.v3, Aspire

**Spec:** `docs/superpowers/specs/2026-03-21-telegram-ux-overhaul-design.md`

---

### Task 1: OrchestrationResult record and IAWConstants updates

**Files:**
- Create: `src/Core/Contracts/OrchestrationResult.cs`
- Modify: `src/Core/IAWConstants.cs`

- [ ] **Step 1: Write the failing test for OrchestrationResult serialization**

Create `test/Core.Tests/OrchestrationResultTests.cs`:

```csharp
using System.Text.Json;
using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests;

public class OrchestrationResultTests
{
    [Fact]
    public void OrchestrationResult_RoundTrips_ViaJson()
    {
        var result = new OrchestrationResult(
            Success: true,
            Summary: "Built successfully",
            WorkspacePath: @"D:\IAW\Calc",
            Artifacts: ["D:\\IAW\\Calc\\App.csproj"],
            Metrics: new() { ["duration"] = "12.4s" },
            ErrorDetail: null,
            TaskId: "2026-03-21-test-task-abc123");

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<OrchestrationResult>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal("Built successfully", deserialized.Summary);
        Assert.Single(deserialized.Artifacts);
        Assert.Null(deserialized.ErrorDetail);
    }

    [Fact]
    public void OrchestrationResult_Failure_PreservesErrorDetail()
    {
        var result = new OrchestrationResult(
            Success: false,
            Summary: "Build failed",
            WorkspacePath: @"D:\workspace\tasks\test",
            Artifacts: [],
            Metrics: null,
            ErrorDetail: "CS1002: ; expected at Form1.cs:42");

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<OrchestrationResult>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("CS1002: ; expected at Form1.cs:42", deserialized.ErrorDetail);
    }

    [Fact]
    public void OrchestrationResult_Deserialize_FallsBackGracefully()
    {
        var plainText = "This is not JSON";
        var parsed = TryParseOrchestrationResult(plainText);
        Assert.Null(parsed);
    }

    static OrchestrationResult? TryParseOrchestrationResult(string text)
    {
        try { return JsonSerializer.Deserialize<OrchestrationResult>(text); }
        catch (JsonException) { return null; }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OrchestrationResultTests" -v minimal`
Expected: FAIL — `OrchestrationResult` type does not exist

- [ ] **Step 3: Create OrchestrationResult record**

Create `src/Core/Contracts/OrchestrationResult.cs`:

```csharp
namespace Core.Contracts;

[GenerateSerializer]
public record OrchestrationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Summary,
    [property: Id(2)] string WorkspacePath,
    [property: Id(3)] List<string> Artifacts,
    [property: Id(4)] Dictionary<string, string>? Metrics,
    [property: Id(5)] string? ErrorDetail,
    [property: Id(6)] string? TaskId = null);
```

The `TaskId` field carries the CodeOrchestrator's internal task slug (e.g. `2026-03-21-user-request-create-a-144e78`). This is the same ID used in `OrchestrationProgress` events. `SendJobResultAsync` extracts it to look up the progress message for in-place editing.

- [ ] **Step 4: Update IAWConstants — add payload keys, remove OrchestrationCompleted**

In `src/Core/IAWConstants.cs`:

Remove `OrchestrationCompleted` from `Events`:
```csharp
// DELETE this line:
public const string OrchestrationCompleted = "orchestration.completed";
```

Add new constants to `PayloadKeys`:
```csharp
public static class PayloadKeys
{
    public const string ProjectKey = "projectKey";
    public const string JobName = "jobName";
    public const string Result = "result";
    public const string TaskId = "taskId";
    public const string Phase = "phase";
    public const string Message = "message";
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OrchestrationResultTests" -v minimal`
Expected: 3 PASS

- [ ] **Step 6: Commit**

```bash
git add src/Core/Contracts/OrchestrationResult.cs src/Core/IAWConstants.cs test/Core.Tests/OrchestrationResultTests.cs
git commit -m "feat: add OrchestrationResult record and new PayloadKeys constants"
```

---

### Task 2: Fix payload key casing bug in Agent.Scheduling

**Files:**
- Modify: `src/Core/Agents/Agent.Scheduling.cs:132-136`

- [ ] **Step 1: Write the failing test for payload key casing**

Add to `test/Core.Tests/AgentTests.cs`:

```csharp
[Fact]
public void PayloadKeys_UseCamelCase()
{
    Assert.Equal("projectKey", IAWConstants.PayloadKeys.ProjectKey);
    Assert.Equal("jobName", IAWConstants.PayloadKeys.JobName);
    Assert.Equal("result", IAWConstants.PayloadKeys.Result);
    Assert.Equal("taskId", IAWConstants.PayloadKeys.TaskId);
    Assert.Equal("phase", IAWConstants.PayloadKeys.Phase);
    Assert.Equal("message", IAWConstants.PayloadKeys.Message);
}
```

- [ ] **Step 2: Run test to verify it passes (constants were already set in Task 1)**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~PayloadKeys_UseCamelCase" -v minimal`
Expected: PASS

- [ ] **Step 3: Fix the casing bug in Agent.Scheduling.OnScheduledJobDueAsync**

In `src/Core/Agents/Agent.Scheduling.cs`, replace lines 132-136:

```csharp
// BEFORE:
await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, string>
{
    ["JobName"] = job.Name,
    ["Result"] = result
}, ct);

// AFTER:
await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, string>
{
    [IAWConstants.PayloadKeys.ProjectKey] = this.GetPrimaryKeyString(),
    [IAWConstants.PayloadKeys.JobName] = job.Name,
    [IAWConstants.PayloadKeys.Result] = result
}, ct);
```

- [ ] **Step 4: Run full test suite to verify no regressions**

Run: `dotnet test test/Core.Tests -v minimal`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Agents/Agent.Scheduling.cs test/Core.Tests/AgentTests.cs
git commit -m "fix: use PayloadKeys constants in base Agent.Scheduling job.completed event"
```

---

### Task 3: Update ICodeOrchestrator interface and CodeOrchestratorAgent return type

**Files:**
- Modify: `src/Core/Contracts/ICodeOrchestrator.cs`
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`
- Modify: `test/Core.Tests/CodeOrchestratorTests.cs`

- [ ] **Step 1: Update ICodeOrchestrator signature**

In `src/Core/Contracts/ICodeOrchestrator.cs`, replace line 14:

```csharp
// BEFORE:
[ResponseTimeout("00:15:00")]
Task<string> ExecuteCodeOrchestration(string plan, IReadOnlyList<string> selectedAgents, CancellationToken ct = default);

// AFTER:
[ResponseTimeout("00:15:00")]
Task<OrchestrationResult> ExecuteCodeOrchestration(string plan, IReadOnlyList<string> selectedAgents, string projectKey, CancellationToken ct = default);
```

- [ ] **Step 2: Update CodeOrchestratorAgent.ExecuteCodeOrchestration to return OrchestrationResult**

In `src/Agents/Orchestration/CodeOrchestratorAgent.cs`:

a) Update `GetResponse` override (lines 182-187):

```csharp
public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
{
    if (prompt.StartsWith("[EXECUTE_CODE]"))
    {
        var result = await ExecuteCodeOrchestration(prompt["[EXECUTE_CODE]\n".Length..], [], "", ct);
        return result.Success
            ? $"Completed. Workspace: {result.WorkspacePath}\nSummary: {result.Summary}"
            : $"Failed. {result.ErrorDetail ?? result.Summary}";
    }
    return await base.GetResponse(prompt, ct);
}
```

b) Update `ExecuteCodeOrchestration` signature (line 189):

```csharp
public async Task<OrchestrationResult> ExecuteCodeOrchestration(string prompt, IReadOnlyList<string> selectedAgents, string projectKey, CancellationToken ct = default)
```

c) Replace all `return` statements in `ExecuteCodeOrchestration` to return `OrchestrationResult` instead of strings. There are 5 return points:

**Important:** All return points include `TaskId: taskId` so `SendJobResultAsync` can match progress messages. The `taskId` variable is the orchestrator's slug (e.g. `2026-03-21-user-request-abc123`), the same ID used in progress events.

Build failure after retries (line 223):
```csharp
return new OrchestrationResult(false, $"Code generation failed after {maxRetries + 1} attempts", taskDir, [], null, buildErrors, taskId);
```

Execution failure (line 236):
```csharp
return new OrchestrationResult(false, $"Code execution failed (exit code {exitCode})", taskDir, [], null, errorSummary, taskId);
```

Success with result.json (lines 246-251):
```csharp
var resultJson = await File.ReadAllTextAsync(resultPath, ct);
var parsed = ParseResultJson(resultJson);
return new OrchestrationResult(
    parsed.GetValueOrDefault("status")?.ToString() != "failed",
    parsed.GetValueOrDefault("summary")?.ToString() ?? "Completed",
    taskDir,
    ParseArtifacts(parsed),
    ParseMetrics(parsed),
    null,
    taskId);
```

Success without result.json (lines 253-254):
```csharp
var lastOutput = log.Length > 1000 ? log[^1000..] : log;
return new OrchestrationResult(true, lastOutput, taskDir, [], null, null, taskId);
```

Exception handler (line 258):
```csharp
return new OrchestrationResult(false, $"CodeOrchestrator error: {ex.GetType().Name}", "", [], null, $"{ex.Message}\n{ex.StackTrace}");
```
Note: exception handler has no `taskId` in scope (it catches before assignment). `TaskId` defaults to `null`.

d) Add helper methods at the bottom of the class:

```csharp
private static Dictionary<string, object?> ParseResultJson(string json)
{
    try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? []; }
    catch { return []; }
}

private static List<string> ParseArtifacts(Dictionary<string, object?> parsed)
{
    if (!parsed.TryGetValue("artifacts", out var val) || val is not System.Text.Json.JsonElement el) return [];
    if (el.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
    return [.. el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)];
}

private static Dictionary<string, string>? ParseMetrics(Dictionary<string, object?> parsed)
{
    if (!parsed.TryGetValue("metrics", out var val) || val is not System.Text.Json.JsonElement el) return null;
    if (el.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
    var dict = new Dictionary<string, string>();
    foreach (var prop in el.EnumerateObject())
        dict[prop.Name] = prop.Value.ToString();
    return dict.Count > 0 ? dict : null;
}
```

- [ ] **Step 3: Update existing tests for new signature**

In `test/Core.Tests/CodeOrchestratorTests.cs`:

Replace `ExecuteCodeOrchestration_CreatesWorkspaceFiles` (lines 20-57):
```csharp
[Fact]
public async Task ExecuteCodeOrchestration_CreatesWorkspaceFiles()
{
    var ct = TestContext.Current.CancellationToken;
    var testWorkspace = Path.Combine(Path.GetTempPath(), $"iaw-test-{Guid.NewGuid():N}");
    Environment.SetEnvironmentVariable("IAW__Workspace", testWorkspace);

    try
    {
        var orchestrator = (ICodeOrchestrator)Agent(UniqueId("orch"));
        var result = await orchestrator.ExecuteCodeOrchestration(
            "INTENT: Test. STEPS: 1. Print hello", new List<string> { "IShell" }, "", ct);

        Assert.NotNull(result);
        Assert.NotEmpty(result.WorkspacePath);

        var tasksDir = Path.Combine(testWorkspace, "tasks");
        Assert.True(Directory.Exists(tasksDir), $"Tasks dir should exist at {tasksDir}. Summary: {result.Summary}");

        var taskDirs = Directory.GetDirectories(tasksDir);
        Assert.Single(taskDirs);

        var taskDir = taskDirs[0];
        Assert.True(File.Exists(Path.Combine(taskDir, "plan.md")));
        Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.cs")));
        Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.csproj")));
        Assert.True(File.Exists(Path.Combine(taskDir, "log.txt")));
    }
    finally
    {
        Environment.SetEnvironmentVariable("IAW__Workspace", null);
        if (Directory.Exists(testWorkspace))
            Directory.Delete(testWorkspace, recursive: true);
    }
}
```

Replace `ExecuteCodeOrchestration_ReturnsErrorOnBadPath` (lines 59-76):
```csharp
[Fact]
public async Task ExecuteCodeOrchestration_ReturnsErrorOnBadPath()
{
    var ct = TestContext.Current.CancellationToken;
    Environment.SetEnvironmentVariable("IAW__Workspace", "Z:\\nonexistent\\path");

    try
    {
        var orchestrator = (ICodeOrchestrator)Agent(UniqueId("orch-err"));
        var result = await orchestrator.ExecuteCodeOrchestration("test plan", new List<string> { "IShell" }, "", ct);

        Assert.False(result.Success);
        Assert.Contains("error", result.Summary, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
        Environment.SetEnvironmentVariable("IAW__Workspace", null);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeOrchestratorTests" -v minimal`
Expected: All PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Contracts/ICodeOrchestrator.cs src/Agents/Orchestration/CodeOrchestratorAgent.cs test/Core.Tests/CodeOrchestratorTests.cs
git commit -m "feat: return OrchestrationResult from CodeOrchestrator instead of raw string"
```

---

### Task 4: Add progress event publishing to CodeOrchestratorAgent

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

- [ ] **Step 1: Store projectKey and publish progress events**

In `CodeOrchestratorAgent.ExecuteCodeOrchestration`, after the `Directory.CreateDirectory(taskDir)` call (line 201), add a helper method call and insert progress publishing at each phase.

Add a private helper:

```csharp
private async Task PublishProgress(string projectKey, string taskId, string phase, string message, CancellationToken ct)
{
    if (string.IsNullOrEmpty(projectKey)) return;
    await PublishAsync(IAWConstants.Events.OrchestrationProgress, new Dictionary<string, string>
    {
        [IAWConstants.PayloadKeys.ProjectKey] = projectKey,
        [IAWConstants.PayloadKeys.TaskId] = taskId,
        [IAWConstants.PayloadKeys.Phase] = phase,
        [IAWConstants.PayloadKeys.Message] = message
    }, ct);
}
```

Insert calls at each phase in `ExecuteCodeOrchestration`:

After `Directory.CreateDirectory` (planning phase):
```csharp
await PublishProgress(projectKey, taskId, "planning", "Generating orchestration code...", ct);
```

Before `TryBuild` in the retry loop:
```csharp
await PublishProgress(projectKey, taskId, "building", $"Building (attempt {attempt + 1})...", ct);
```

Before `ExecuteProject`:
```csharp
await PublishProgress(projectKey, taskId, "executing", "Running orchestration...", ct);
```

On retry (when `attempt < maxRetries` and errors found):
```csharp
await PublishProgress(projectKey, taskId, "retrying", $"Fixing build errors (attempt {attempt + 1})...", ct);
```

- [ ] **Step 2: Run tests to verify no regressions**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeOrchestratorTests" -v minimal`
Expected: All PASS (progress events fire but no subscriber in test)

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "feat: publish OrchestrationProgress events from CodeOrchestrator at each phase"
```

---

### Task 5: Update ThreadAgent — verbatim message preservation and OrchestrationResult serialization

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`
- Modify: `src/Agents/Orchestration/IThread.cs`

- [ ] **Step 1: Write test for verbatim message preservation**

Add to `test/Core.Tests/ThreadTests.cs`:

```csharp
[Fact]
public async Task ExecuteSelection_UsesVerbatimUserMessage_FromHistory()
{
    var ct = TestContext.Current.CancellationToken;
    var thread = Agent(UniqueId("verbatim"));

    // Send a message with a specific path so it's in history
    await thread.GetResponse(@"Create a calculator at D:\IAW\Calc", ct);
    var history = await thread.GetHistory(ct);

    var lastUserMsg = history.LastOrDefault(m => m.Role == "user");
    Assert.NotNull(lastUserMsg);
    Assert.Contains(@"D:\IAW\Calc", lastUserMsg.Text);
}
```

- [ ] **Step 2: Run test to verify it passes (history already captures verbatim text)**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ExecuteSelection_UsesVerbatimUserMessage" -v minimal`
Expected: PASS

- [ ] **Step 3: Update ExecuteSelection in ThreadAgent**

In `src/Agents/Orchestration/ThreadAgent.cs`, add `using System.Text.Json;` at the top, then replace the `ExecuteSelection` method (lines 66-84):

```csharp
private async Task<string> ExecuteSelection(SelectionResult selection, string request, CancellationToken ct)
{
    var threadId = this.GetPrimaryKeyString();
    var lastUserMsg = durableState.History.LastOrDefault(m => m.Role == "user");
    var userMessage = lastUserMsg?.Text ?? request;

    if (selection.SelectedAgents.Count == 1)
    {
        var agentInterfaceName = selection.SelectedAgents[0];
        var interfaceType = AgentInterfaceResolver.Resolve(agentInterfaceName);
        if (interfaceType is null)
            return $"Could not resolve agent: {agentInterfaceName}";

        var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");
        return await agent.GetResponse(request, ct);
    }

    var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
    var selectorPlan = selection.Plan ?? $"Agents: {string.Join(", ", selection.SelectedAgents)}";
    var plan = $"USER REQUEST: {userMessage}\n\nPLAN:\n{selectorPlan}";
    var result = await orchestrator.ExecuteCodeOrchestration(plan, selection.SelectedAgents, threadId, ct);
    return JsonSerializer.Serialize(result);
}
```

- [ ] **Step 4: Update IThread.AgentInstructions to preserve paths**

In `src/Agents/Orchestration/IThread.cs`, update `AgentInstructions` — add a line about preserving paths:

```csharp
static string IAgent.AgentInstructions => """
    You are an AI assistant in the IAW (Interactive Agents Workspace) system —
    a multi-agent platform built on Orleans. You have access to a team of
    specialized agents that can execute tasks: coding, git, shell, .NET builds,
    code review, and more.

    DECISION RULE:
    - Answer directly when: greetings, general knowledge, questions about
      conversation context, user preferences, or anything you can answer
      from your enriched context
    - Use the Delegate tool when: the request involves code execution,
      system operations, agent capabilities, builds, git, file operations,
      or anything requiring specialized agent skills

    When delegating, describe WHAT needs to be done, not HOW. The agent
    system handles routing and execution automatically.
    ALWAYS preserve exact paths, filenames, and locations from the user's message.
    If the user says "at D:\MyApp", include that exact path in your delegation.

    Be concise and direct. Use markdown formatting.
    """;
```

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadTests|FullyQualifiedName~ThreadDelegateToolTests" -v minimal`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs src/Agents/Orchestration/IThread.cs test/Core.Tests/ThreadTests.cs
git commit -m "feat: preserve verbatim user message in ThreadAgent delegation and serialize OrchestrationResult"
```

---

### Task 6: Add SendProgressAsync to TelegramBotService

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

- [ ] **Step 1: Add the progress message tracking field and SendProgressAsync method**

In `src/Clients.Telegram/TelegramBotService.cs`:

Add field after line 28 (after `static readonly` declarations):

```csharp
private readonly ConcurrentDictionary<string, (long ChatId, int MessageId, int? TopicId)> _progressMessages = new();
```

Add `using System.Collections.Concurrent;` if not already imported.

Add the new method after `SendJobResultAsync`:

```csharp
public async Task SendProgressAsync(string projectKey, string taskId, string phase, string message, CancellationToken ct)
{
    if (_progressMessages.TryGetValue(taskId, out var existing))
    {
        try
        {
            await botClient.EditMessageTextAsync(existing.ChatId, existing.MessageId, $"\u2699\ufe0f {message}");
        }
        catch (BotRequestException) { }
        return;
    }

    var parts = projectKey.Split('/');
    if (parts.Length < 2 || !long.TryParse(parts[0], out _))
    {
        logger.LogWarning("SendProgress: invalid projectKey format '{ProjectKey}'", projectKey);
        return;
    }

    var userId = parts[0];
    var slug = parts[1];
    var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
    var prefs = await userProfile.GetPreferences(ct);
    if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
    {
        logger.LogWarning("SendProgress: no GroupChatId for user {UserId}", userId);
        return;
    }

    var topicId = await userProfile.GetTopicId(slug, ct);
    var sent = await botClient.SendMessageAsync(chatId, $"\u2699\ufe0f {message}", messageThreadId: topicId);
    _progressMessages[taskId] = (chatId, sent.MessageId, topicId);
}

public bool TryGetProgressMessage(string taskId, out (long ChatId, int MessageId, int? TopicId) progress)
    => _progressMessages.TryRemove(taskId, out progress);
```

- [ ] **Step 2: Extract streaming edit interval to a named constant**

In `src/Clients.Telegram/TelegramBotService.cs`, add constant after the existing `static readonly` declarations:

```csharp
const int StreamingEditIntervalMs = 1500;
```

Replace the hardcoded `500` in `StreamResponseAsync` (line 629):

```csharp
// BEFORE:
if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > 500)

// AFTER:
if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > StreamingEditIntervalMs)
```

- [ ] **Step 3: Run build to verify compilation**

Run: `dotnet build src/Clients.Telegram`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: add SendProgressAsync with message tracking and increase streaming interval to 1.5s"
```

---

### Task 7: Refactor SendJobResultAsync for structured results

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

- [ ] **Step 1: Refactor SendJobResultAsync to parse OrchestrationResult and route through TelegramUIAgent**

In `src/Clients.Telegram/TelegramBotService.cs`, replace `SendJobResultAsync` (lines 380-416):

```csharp
public async Task SendJobResultAsync(string projectKey, string jobName, string result, CancellationToken ct)
{
    var parts = projectKey.Split('/');
    if (parts.Length < 2 || !long.TryParse(parts[0], out _))
    {
        logger.LogWarning("SendJobResult: invalid projectKey format '{ProjectKey}'", projectKey);
        return;
    }

    var userId = parts[0];
    var slug = parts[1];
    var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
    var prefs = await userProfile.GetPreferences(ct);
    if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
    {
        logger.LogWarning("SendJobResult: no GroupChatId for user {UserId}, slug {Slug}", userId, slug);
        return;
    }

    var topicId = await userProfile.GetTopicId(slug, ct);
    var telegramId = long.Parse(userId);
    var (formattedText, orchestrationTaskId) = FormatOrchestrationResult(result);

    // look up progress message by the CodeOrchestrator's taskId (not the delegation jobName)
    int messageId;
    if (orchestrationTaskId is not null && TryGetProgressMessage(orchestrationTaskId, out var progress))
    {
        try
        {
            await EditSafe(progress.ChatId, progress.MessageId, formattedText);
            messageId = progress.MessageId;
            chatId = progress.ChatId;
            topicId = progress.TopicId;
        }
        catch
        {
            var sent = await botClient.SendMessageAsync(chatId, formattedText, messageThreadId: topicId);
            messageId = sent.MessageId;
        }
    }
    else
    {
        var sent = await botClient.SendMessageAsync(chatId, formattedText, messageThreadId: topicId);
        messageId = sent.MessageId;
    }

    // route through TelegramUIAgent for buttons
    try
    {
        var uiAgent = clusterClient.GetGrain<ITelegramUI>($"tg-ui-{Guid.NewGuid().ToString("N")[..8]}");
        var richOutput = await uiAgent.FormatResponse(formattedText, ct);

        if (richOutput.Parts.Count > 0)
            await RenderRichOutput(chatId, messageId, topicId, richOutput, telegramId, ct);
        else if (!string.IsNullOrEmpty(richOutput.FormattedText))
            await EditWithMarkdown(chatId, messageId, richOutput.FormattedText);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "TelegramUI formatting failed for job result, keeping plain text");
    }
}

private static (string Text, string? TaskId) FormatOrchestrationResult(string resultPayload)
{
    try
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<Core.Contracts.OrchestrationResult>(resultPayload);
        if (result is null) return (resultPayload, null);

        var sb = new StringBuilder();
        sb.AppendLine(result.Success ? $"\u2705 {result.Summary}" : $"\u274c {result.Summary}");

        foreach (var artifact in result.Artifacts)
            sb.AppendLine($"\ud83d\udcc1 {artifact}");

        if (result.Metrics is { Count: > 0 })
        {
            var metricStr = string.Join(", ", result.Metrics.Select(kv => $"{kv.Key}: {kv.Value}"));
            sb.AppendLine($"\u23f1 {metricStr}");
        }

        if (!result.Success && !string.IsNullOrEmpty(result.ErrorDetail))
        {
            var truncated = result.ErrorDetail.Length > 500 ? result.ErrorDetail[..500] + "..." : result.ErrorDetail;
            sb.AppendLine();
            sb.AppendLine(truncated);
        }

        return (sb.ToString().TrimEnd(), result.TaskId);
    }
    catch (System.Text.Json.JsonException)
    {
        return (resultPayload, null);
    }
}
```

- [ ] **Step 2: Remove old SplitForTelegram method if no longer used**

Check if `SplitForTelegram` (lines 418-437) is used elsewhere. If not, delete it.

- [ ] **Step 3: Run build to verify compilation**

Run: `dotnet build src/Clients.Telegram`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: structured OrchestrationResult formatting with TelegramUIAgent buttons in job results"
```

---

### Task 8: Clean up StreamSubscriber — rewire progress, remove dead subscriptions

**Files:**
- Modify: `src/Clients.Telegram/StreamSubscriber.cs`

- [ ] **Step 1: Rewire orchestration.progress handler to SendProgressAsync**

In `src/Clients.Telegram/StreamSubscriber.cs`, replace the progress handler (lines 113-128):

```csharp
var progressStream = streamProvider.GetStream<AgentEvent>(
    StreamId.Create(IAWConstants.StreamProvider, IAWConstants.Events.OrchestrationProgress));
await progressStream.SubscribeAsync(async (evt, token) =>
{
    try
    {
        var projectKey = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.ProjectKey)?.ToString() ?? "";
        var taskId = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.TaskId)?.ToString() ?? "";
        var phase = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Phase)?.ToString() ?? "";
        var message = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Message)?.ToString() ?? "";
        await botService.SendProgressAsync(projectKey, taskId, phase, message, ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to send orchestration progress to Telegram");
    }
});
```

- [ ] **Step 2: Remove dead subscriptions**

Delete these subscription blocks from `StreamSubscriber.ExecuteAsync`:

- `approvalStream` subscription (lines 44-60)
- `dashboardStream` subscription (lines 62-76)
- `wizardStream` subscription (lines 78-94)
- `completedStream` subscription (lines 130-145)

- [ ] **Step 3: Remove ScheduleDebouncedDashboardUpdate and _dashboardDebounce**

Delete the `_dashboardDebounce` field (line 19) and the entire `ScheduleDebouncedDashboardUpdate` method (lines 167-200).

- [ ] **Step 4: Update the log message at the end**

Replace:
```csharp
logger.LogInformation("Subscribed to agent notification, approval, dashboard, wizard, job completed, and orchestration streams");
```
With:
```csharp
logger.LogInformation("Subscribed to notification, job completed, and orchestration progress streams");
```

- [ ] **Step 5: Run build to verify compilation**

Run: `dotnet build src/Clients.Telegram`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/Clients.Telegram/StreamSubscriber.cs
git commit -m "fix: rewire orchestration.progress to SendProgressAsync, remove dead subscriptions"
```

---

### Task 9: Build and integration smoke test

**Files:** None (verification only)

- [ ] **Step 1: Run full unit test suite**

Run: `dotnet test IAW.slnx -v minimal`
Expected: All PASS

- [ ] **Step 2: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Start Aspire and verify startup**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Verify: All resources start without errors in Aspire dashboard

- [ ] **Step 4: Test via MCP — send a delegation request and verify progress events in Aspire traces**

Use the `assistant_chat` MCP tool to send a task and check Aspire traces for `orchestration.progress` events.

- [ ] **Step 5: Commit any test fixes if needed**

---

### Task 10: Update website/guide/telegram.md

**Files:**
- Modify: `website/guide/telegram.md`

- [ ] **Step 1: Replace IProject with IThread throughout**

Replace all occurrences of `IProject` with `IThread` and `project` variable names with `thread` in code blocks.

- [ ] **Step 2: Update architecture diagram**

Replace the architecture diagram with:
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

- [ ] **Step 3: Replace DelegateToAssistant/PersonalAssistant references**

Replace all `DelegateToAssistant` and `PersonalAssistant` references with `Delegate` tool, `AgentSelectorAgent`, and `CodeOrchestratorAgent`.

- [ ] **Step 4: Update streaming throttle**

Change "500ms throttling" to "1500ms throttling".

- [ ] **Step 5: Add section on progress updates**

Add after "Streaming Responses" or "Message Flow" section:

```markdown
## Delegation Progress

When the Thread agent delegates a task to CodeOrchestrator, real-time progress events update the user:

1. CodeOrchestrator publishes `orchestration.progress` events at each phase (planning, building, executing)
2. StreamSubscriber routes these to `TelegramBotService.SendProgressAsync`
3. The first event sends a new message (triggers push notification)
4. Subsequent events edit that message in-place
5. On completion, the progress message is replaced with a structured result card with follow-up buttons
```

- [ ] **Step 6: Add section on structured results**

```markdown
## Structured Job Results

Delegated task results are formatted as structured cards via `OrchestrationResult`:

- Success/failure icon
- Summary text
- Artifact file paths
- Follow-up suggestion buttons (via TelegramUIAgent)

Results that cannot be parsed as `OrchestrationResult` (e.g., single-agent responses) fall back to the existing TelegramUIAgent formatting.
```

- [ ] **Step 7: Commit**

```bash
git add website/guide/telegram.md
git commit -m "docs: update telegram.md for IThread, delegation flow, progress updates"
```

---

### Task 11: Update website/guide/telegram-features.md

**Files:**
- Modify: `website/guide/telegram-features.md`

- [ ] **Step 1: Replace IProject/DelegateToAssistant/PersonalAssistant references**

Same replacements as Task 10. Replace `IProject` → `IThread`, `DelegateToAssistant` → `Delegate`, `PersonalAssistant` → `ThreadAgent/AgentSelector/CodeOrchestrator`.

- [ ] **Step 2: Update streaming rate**

Change "500ms throttling" to "1500ms throttling".

- [ ] **Step 3: Update Event Streams table**

Replace the event streams table with:

```markdown
| Stream | Event Type | Action |
|--------|-----------|--------|
| `notification.sent` | `AgentEvent` | Sends markdown notification to the configured chat |
| `job.completed` | `AgentEvent` | Formats OrchestrationResult → TelegramUIAgent → RichOutput with buttons |
| `orchestration.progress` | `AgentEvent` | Live progress edits during CodeOrchestrator execution |
```

- [ ] **Step 4: Update Task Delegation section**

Replace the delegation flow diagram:

```markdown
## Task Delegation

The Thread agent's `Delegate` tool forwards complex tasks through the agent selection and orchestration pipeline:

\```
User message → Thread agent → Delegate tool
                                    ↓
                            AgentSelectorAgent (picks agents)
                                    ↓
                ┌───────────────────┴───────────────┐
                │ Single agent                      │ Multi-agent
                │ agent.GetResponse()               │ CodeOrchestrator
                │                                   │ → generates C# → runs agents
                └───────────────────┬───────────────┘
                                    ↓
                            Result → Telegram (structured card + buttons)
\```
```

- [ ] **Step 5: Add section on structured results and buttons**

Add after Task Delegation:

```markdown
## Structured Results

Job results are delivered as structured cards:

- ✅/❌ status icon with summary
- File paths for created artifacts
- Metrics (token usage, duration)
- Follow-up buttons via TelegramUIAgent (e.g., "Run it", "Open folder", "Retry")
```

- [ ] **Step 6: Commit**

```bash
git add website/guide/telegram-features.md
git commit -m "docs: update telegram-features.md for new delegation flow and event streams"
```

---

### Task 12: Update website/guide/orchestration.md

**Files:**
- Modify: `website/guide/orchestration.md`

- [ ] **Step 1: Replace PlanningAgent with CodeOrchestratorAgent**

Replace all `PlanningAgent` references with `CodeOrchestratorAgent`.

- [ ] **Step 2: Remove obsolete sections**

Delete these sections entirely (they reference removed components):
- `OrchestrationPlan` section and its `PlanStep` record
- `ScriptExecutor` section
- `CheckpointStore` section
- `PersonalAssistant as Coordinator` section

- [ ] **Step 3: Update Agent Registry references**

Replace `IAgentRegistryGrain` → `IAgentRegistry`, `AgentRegistration` → `AgentRecord`, `AgentQuery` → updated query patterns.

- [ ] **Step 4: Update full orchestration flow diagram**

Replace with:

```markdown
## Full Orchestration Flow

\```
User: "Create a calculator app at D:\IAW\Calc"
  |
  v
Thread agent → Delegate tool (schedules async job)
  |
  v
AgentSelectorAgent --> picks IFileSystem, IDotNet, IRoslyn
  |
  v
CodeOrchestratorAgent:
  1. Generates standalone C# console app
  2. The app connects to the Orleans cluster via AddIAWClient()
  3. Calls agent grains: shell.RunDotnetAsync(), fs.WriteFileAsync(), etc.
  4. Executes with dotnet run, captures output
  5. Returns OrchestrationResult (success/failure, artifacts, metrics)
  |
  v
Progress events → orchestration.progress stream → Telegram live updates
  |
  v
OrchestrationResult → job.completed stream → structured card + buttons in Telegram
\```
```

- [ ] **Step 5: Add OrchestrationResult section**

```markdown
## OrchestrationResult

The orchestrator returns a structured result:

\```csharp
[GenerateSerializer]
public record OrchestrationResult(
    bool Success,
    string Summary,
    string WorkspacePath,
    List<string> Artifacts,
    Dictionary<string, string>? Metrics,
    string? ErrorDetail);
\```

The result is serialized to JSON in the `job.completed` event payload and parsed by the Telegram client for structured rendering.
```

- [ ] **Step 6: Add progress events section**

```markdown
## Progress Events

During execution, the CodeOrchestrator publishes `orchestration.progress` events at each phase:

| Phase | When |
|-------|------|
| `planning` | After generating the C# orchestration code |
| `building` | Before compiling the generated code |
| `retrying` | When fixing build errors and retrying |
| `executing` | Before running the compiled orchestration |

These events are consumed by the Telegram StreamSubscriber for live progress updates.
```

- [ ] **Step 7: Commit**

```bash
git add website/guide/orchestration.md
git commit -m "docs: rewrite orchestration.md for CodeOrchestrator, OrchestrationResult, progress events"
```

---

### Task 13: Update website/guide/events-streams.md

**Files:**
- Modify: `website/guide/events-streams.md`

- [ ] **Step 1: Replace PublishTypedAsync with PublishToStream<T>**

Replace all `PublishTypedAsync` references with `PublishToStream<T>`.

- [ ] **Step 2: Update combined flow example**

Replace `PersonalAssistantAgent` references with `ThreadAgent` and `CodeOrchestratorAgent`. Update the combined flow example code to use current agent names.

- [ ] **Step 3: Verify stream name resolution table**

Check that all type → stream name mappings are still accurate. Update any that reference removed types.

- [ ] **Step 4: Commit**

```bash
git add website/guide/events-streams.md
git commit -m "docs: update events-streams.md for current agent names and PublishToStream"
```

---

### Task 14: Final verification

**Files:** None (verification only)

- [ ] **Step 1: Run full test suite**

Run: `dotnet test IAW.slnx -v minimal`
Expected: All PASS

- [ ] **Step 2: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors

- [ ] **Step 3: Verify website builds (if applicable)**

Run: `cd website && npm run build`
Expected: Build succeeded

- [ ] **Step 4: Start Aspire, send a Telegram message, verify progress + structured result**

Start: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Test: Send "Create a calculator at D:\IAW\Calc" via Telegram
Verify:
1. Thread responds with acknowledgment
2. Progress message appears: "⚙️ Generating orchestration code..."
3. Progress edits in-place through phases
4. Final result shows structured card with buttons

- [ ] **Step 5: Commit any final fixes**
