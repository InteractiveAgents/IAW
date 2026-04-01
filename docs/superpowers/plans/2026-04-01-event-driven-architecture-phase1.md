# Event-Driven Architecture — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix history durability, implement Task Ledger grain, define typed event schema, and integrate ledger as context provider — the foundation for all event-driven communication.

**Architecture:** Phase 1 covers the P0 issues (#20, #21, #25, #27) that everything else builds on. The Task Ledger is a new grain with a durable event list. The typed event schema replaces scattered string constants. The history fix ensures messages persist immediately. All changes are in Core.

**Tech Stack:** Orleans 10.0.1, Orleans.Journaling alpha, .NET 11 preview, xunit.v3

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `src/Core/Contracts/TaskEvent.cs` | Structured event record for task ledger (~50 tokens per event) |
| `src/Core/Contracts/ITaskLedger.cs` | Grain interface for the per-task event log |
| `src/Core/Grains/TaskLedgerGrain.cs` | Grain implementation with IDurableList<TaskEvent> |
| `src/Core/Context/TaskLedgerContextProvider.cs` | IAgentContextProvider that reads ledger and formats as prompt context |
| `src/Core/Contracts/Events/AgentEventType.cs` | Typed event constants replacing scattered strings |
| `test/Core.Tests/HistoryDurabilityTests.cs` | Tests for history persistence through deactivation |
| `test/Core.Tests/TaskLedgerTests.cs` | Tests for TaskLedger grain CRUD and context formatting |
| `test/Core.Tests/TypedEventTests.cs` | Tests for typed event schema |

### Modified Files
| File | Change |
|------|--------|
| `src/Core/Agents/DurableChatHistoryProvider.cs` | Accept persist callback, call it after adding messages |
| `src/Core/Agents/HistorySummarizer.cs` | Accept durable state dict for persisting _lastSummary |
| `src/Core/Agents/Agent.cs:55-81` | Pass persist callback and state dict to providers |
| `src/Core/Agents/Agent.Events.cs` | Add typed `PublishAsync(TaskEvent)` overload for ledger |
| `src/Core/Contracts/AgentDurableState.cs` | No change needed — summary stored via existing State dict |
| `src/Core/IAWConstants.cs` | Add TaskLedger grain type constant |

---

## Task 1: Fix Conversation History Durability (#20)

**Files:**
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs`
- Modify: `src/Core/Agents/HistorySummarizer.cs`
- Modify: `src/Core/Agents/Agent.cs:55-81`
- Create: `test/Core.Tests/HistoryDurabilityTests.cs`

- [ ] **Step 1: Write failing test — history persists after adding messages**

Create `test/Core.Tests/HistoryDurabilityTests.cs`:

```csharp
using Core.Contracts;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests;

public class HistoryDurabilityTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task History_PersistsAfterEachMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("hist-persist"));

        // send a message — this triggers StoreChatHistoryAsync
        await agent.GetResponse("Hello", ct);

        // get history — should have user message + assistant response
        var history = await agent.GetHistory(ct);
        Assert.True(history.Count >= 2, $"Expected at least 2 messages, got {history.Count}");
    }

    [Fact]
    public async Task History_SurvivesGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("hist-deactivate");
        var agent = Agent(agentId);

        await agent.GetResponse("Remember this message", ct);
        var historyBefore = await agent.GetHistory(ct);
        var countBefore = historyBefore.Count;
        Assert.True(countBefore >= 2);

        // force deactivation by deactivating all grains on the silo
        await Cluster.Primary.TestHook.DeactivateAllGrainsInSilo();

        // reactivate by calling the grain again
        var agent2 = Agent(agentId);
        var historyAfter = await agent2.GetHistory(ct);

        Assert.Equal(countBefore, historyAfter.Count);
        Assert.Contains(historyAfter, m => m.Content!.Contains("Remember this message"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~HistoryDurabilityTests" -v n
```

Expected: `History_SurvivesGrainDeactivation` FAILS — messages lost after deactivation because `StoreChatHistoryAsync` doesn't call `WriteStateAsync`.

- [ ] **Step 3: Fix DurableChatHistoryProvider — accept persist callback**

Modify `src/Core/Agents/DurableChatHistoryProvider.cs`. Change the constructor to accept a persist callback, and call it at the end of `StoreChatHistoryAsync`:

```csharp
internal sealed class DurableChatHistoryProvider(
    IDurableList<ChatMessage> history,
    int maxMessages,
    Func<CancellationToken, Task> persistCallback,
    BlobFileStorage? blobStorage = null,
    ChatReducer? reducer = null,
    HistorySummarizer? summarizer = null) : ChatHistoryProvider
{
    // ... existing fields and ProvideChatHistoryAsync unchanged ...

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var message in context.RequestMessages)
        {
            var text = message.Text ?? string.Empty;
            history.Add(new ChatMessage
            {
                Role = message.Role.Value,
                Content = text,
                Parts = [new Contracts.TextContent(text)]
            });
        }

        foreach (var message in context.ResponseMessages ?? [])
        {
            var text = message.Text ?? string.Empty;
            history.Add(new ChatMessage
            {
                Role = message.Role.Value,
                Content = text,
                Parts = [new Contracts.TextContent(text)]
            });
        }

        await persistCallback(cancellationToken);
    }
}
```

- [ ] **Step 4: Update Agent.cs to pass persist callback**

In `src/Core/Agents/Agent.cs`, change line 71 to pass `WriteStateAsync` as the persist callback:

```csharp
ChatHistoryProvider = new DurableChatHistoryProvider(
    durableState.History,
    MaxHistoryMessages,
    ct => WriteStateAsync(ct),
    blobStorage,
    new ChatReducer(),
    new HistorySummarizer(chatClient))
```

- [ ] **Step 5: Make HistorySummarizer summary durable**

Modify `src/Core/Agents/HistorySummarizer.cs` to accept the state dictionary and persist the summary:

```csharp
internal sealed class HistorySummarizer(
    IChatClient chatClient,
    IDurableDictionary<string, StateEntry>? durableState = null)
{
    private const int SummarizationThreshold = 40;
    private const int RecentWindow = 20;
    private const string SummaryStateKey = "__history_summary";
    private const string SummaryEndKey = "__history_summary_end";

    private int _lastSummarizedOldEnd;
    private ChatMessage? _cachedSummary;

    public async Task<ChatMessage?> SummarizeIfNeededAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? existingSummary,
        CancellationToken ct = default)
    {
        // on first call, try to restore from durable state
        if (_cachedSummary is null && existingSummary is null && durableState is not null)
        {
            if (durableState.TryGetValue(SummaryStateKey, out var entry))
            {
                _cachedSummary = new ChatMessage
                {
                    Role = "system",
                    Content = entry.Value.ToString()!,
                    Parts = [new Contracts.TextContent(entry.Value.ToString()!)]
                };
                if (durableState.TryGetValue(SummaryEndKey, out var endEntry)
                    && int.TryParse(endEntry.Value.ToString(), out var savedEnd))
                {
                    _lastSummarizedOldEnd = savedEnd;
                }
                existingSummary = _cachedSummary;
            }
        }

        if (history.Count <= SummarizationThreshold)
            return existingSummary;

        var oldEnd = history.Count - RecentWindow;

        if (existingSummary is not null && oldEnd <= _lastSummarizedOldEnd)
            return existingSummary;

        var messagesToSummarize = new List<ChatMessage>();
        for (var i = 0; i < oldEnd; i++)
        {
            if (!ChatReducer.IsNonReducible(history[i]))
                messagesToSummarize.Add(history[i]);
        }

        if (messagesToSummarize.Count == 0)
            return existingSummary;

        var conversationText = string.Join("\n", messagesToSummarize.Select(m => $"{m.Role}: {m.Text}"));
        var prompt = $"""
            Summarize this conversation history concisely, preserving key decisions, task assignments, and outcomes.
            Do not include greetings or small talk.

            Conversation:
            {conversationText}
            """;

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>();
        if (existingSummary is not null)
            messages.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, $"Previous summary: {existingSummary.Text}"));
        messages.Add(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt));

        try
        {
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var summaryText = response.Text ?? "";

            _lastSummarizedOldEnd = oldEnd;
            var summary = new ChatMessage
            {
                Role = "system",
                Content = $"[Conversation summary] {summaryText}",
                Parts = [new Contracts.TextContent($"[Conversation summary] {summaryText}")]
            };

            // persist to durable state
            if (durableState is not null)
            {
                durableState[SummaryStateKey] = new StateEntry(SummaryStateKey, summary.Content);
                durableState[SummaryEndKey] = new StateEntry(SummaryEndKey, oldEnd);
            }

            _cachedSummary = summary;
            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return existingSummary;
        }
    }
}
```

- [ ] **Step 6: Update Agent.cs to pass state dict to HistorySummarizer**

In `src/Core/Agents/Agent.cs`, update line 71:

```csharp
ChatHistoryProvider = new DurableChatHistoryProvider(
    durableState.History,
    MaxHistoryMessages,
    ct => WriteStateAsync(ct),
    blobStorage,
    new ChatReducer(),
    new HistorySummarizer(chatClient, durableState.State))
```

- [ ] **Step 7: Run tests**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~HistoryDurabilityTests" -v n
```

Expected: PASS

- [ ] **Step 8: Build full solution**

```bash
dotnet build IAW.slnx
```

Expected: 0 errors, 0 warnings (TreatWarningsAsErrors is on)

- [ ] **Step 9: Run all existing tests to verify no regressions**

```bash
dotnet test IAW.slnx -v n
```

Expected: All tests pass

- [ ] **Step 10: Commit**

```bash
git add src/Core/Agents/DurableChatHistoryProvider.cs src/Core/Agents/HistorySummarizer.cs src/Core/Agents/Agent.cs test/Core.Tests/HistoryDurabilityTests.cs
git commit -m "fix: persist chat history immediately in StoreChatHistoryAsync (#20)

- DurableChatHistoryProvider now calls WriteStateAsync after adding messages
- HistorySummarizer stores summary in durable state dict (survives deactivation)
- Added HistoryDurabilityTests verifying persistence through grain deactivation"
```

---

## Task 2: Define Typed Event Schema (#25)

**Files:**
- Create: `src/Core/Contracts/Events/AgentEventType.cs`
- Create: `src/Core/Contracts/TaskEvent.cs`
- Create: `test/Core.Tests/TypedEventTests.cs`
- Modify: `src/Core/IAWConstants.cs`

- [ ] **Step 1: Write failing tests for TaskEvent record and AgentEventType**

Create `test/Core.Tests/TypedEventTests.cs`:

```csharp
using Core.Contracts;
using Core.Contracts.Events;
using Xunit;

namespace Core.Tests;

public class TypedEventTests
{
    [Fact]
    public void TaskEvent_HasRequiredFields()
    {
        var evt = new TaskEvent(
            Agent: "DotNet",
            Action: AgentEventType.BuildSucceeded,
            Result: "0 warnings",
            Detail: "net11.0 Release",
            Timestamp: DateTimeOffset.UtcNow);

        Assert.Equal("DotNet", evt.Agent);
        Assert.Equal(AgentEventType.BuildSucceeded, evt.Action);
        Assert.Equal("0 warnings", evt.Result);
    }

    [Fact]
    public void TaskEvent_TextRepresentation_IsCompact()
    {
        var evt = new TaskEvent(
            Agent: "FileSystem",
            Action: AgentEventType.FileCreated,
            Result: "budget.xlsx created",
            Detail: null,
            Timestamp: DateTimeOffset.UtcNow);

        var text = evt.ToContextLine();
        Assert.Contains("FileSystem", text);
        Assert.Contains("budget.xlsx created", text);
        // must be compact — under 120 chars for ledger context injection
        Assert.True(text.Length < 120, $"Context line too long: {text.Length} chars");
    }

    [Fact]
    public void AgentEventType_CoversCoreDomains()
    {
        // verify key event types exist as constants
        Assert.Equal("build.succeeded", AgentEventType.BuildSucceeded);
        Assert.Equal("build.failed", AgentEventType.BuildFailed);
        Assert.Equal("file.created", AgentEventType.FileCreated);
        Assert.Equal("file.read", AgentEventType.FileRead);
        Assert.Equal("test.passed", AgentEventType.TestPassed);
        Assert.Equal("test.failed", AgentEventType.TestFailed);
        Assert.Equal("commit.created", AgentEventType.CommitCreated);
        Assert.Equal("validation.passed", AgentEventType.ValidationPassed);
        Assert.Equal("validation.failed", AgentEventType.ValidationFailed);
        Assert.Equal("task.created", AgentEventType.TaskCreated);
        Assert.Equal("task.completed", AgentEventType.TaskCompleted);
        Assert.Equal("step.completed", AgentEventType.StepCompleted);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~TypedEventTests" -v n
```

Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Create AgentEventType constants**

Create `src/Core/Contracts/Events/AgentEventType.cs`:

```csharp
namespace Core.Contracts.Events;

public static class AgentEventType
{
    // build
    public const string BuildSucceeded = "build.succeeded";
    public const string BuildFailed = "build.failed";

    // test
    public const string TestPassed = "test.passed";
    public const string TestFailed = "test.failed";

    // file
    public const string FileCreated = "file.created";
    public const string FileRead = "file.read";
    public const string FileWritten = "file.written";

    // git
    public const string CommitCreated = "commit.created";
    public const string RevertCompleted = "revert.completed";

    // orchestration
    public const string TaskCreated = "task.created";
    public const string TaskCompleted = "task.completed";
    public const string StepCompleted = "step.completed";
    public const string StepFailed = "step.failed";

    // validation
    public const string ValidationPassed = "validation.passed";
    public const string ValidationFailed = "validation.failed";

    // scheduling
    public const string JobCompleted = "job.completed";

    // system
    public const string HealthWarning = "health.warning";
    public const string HealthCritical = "health.critical";
    public const string ApprovalRequested = "approval.requested";

    // knowledge
    public const string DecisionRecorded = "decision.recorded";

    // deployment
    public const string DeploySucceeded = "deploy.succeeded";
    public const string DeployFailed = "deploy.failed";
}
```

- [ ] **Step 4: Create TaskEvent record**

Create `src/Core/Contracts/TaskEvent.cs`:

```csharp
namespace Core.Contracts;

[GenerateSerializer]
public record TaskEvent(
    [property: Id(0)] string Agent,
    [property: Id(1)] string Action,
    [property: Id(2)] string Result,
    [property: Id(3)] string? Detail,
    [property: Id(4)] DateTimeOffset Timestamp)
{
    // compact one-line representation for ledger context injection (~50 tokens max)
    public string ToContextLine()
    {
        var detail = Detail is not null ? $" ({Detail})" : "";
        return $"- {Agent}: {Result}{detail}";
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~TypedEventTests" -v n
```

Expected: PASS

- [ ] **Step 6: Add TaskLedger grain type to IAWConstants**

In `src/Core/IAWConstants.cs`, add to the `GrainTypes` class:

```csharp
public const string TaskLedger = "task-ledger";
```

- [ ] **Step 7: Build and run all tests**

```bash
dotnet build IAW.slnx && dotnet test IAW.slnx -v n
```

Expected: All pass

- [ ] **Step 8: Commit**

```bash
git add src/Core/Contracts/Events/AgentEventType.cs src/Core/Contracts/TaskEvent.cs src/Core/IAWConstants.cs test/Core.Tests/TypedEventTests.cs
git commit -m "feat: add typed event schema and TaskEvent record (#25)

- AgentEventType static class replaces scattered hardcoded event strings
- TaskEvent record with compact ToContextLine() for ledger injection
- TaskLedger grain type constant added to IAWConstants"
```

---

## Task 3: Implement Task Ledger Grain (#21)

**Files:**
- Create: `src/Core/Contracts/ITaskLedger.cs`
- Create: `src/Core/Grains/TaskLedgerGrain.cs`
- Create: `test/Core.Tests/TaskLedgerTests.cs`

- [ ] **Step 1: Write failing tests for TaskLedger grain**

Create `test/Core.Tests/TaskLedgerTests.cs`:

```csharp
using Core.Contracts;
using Core.Contracts.Events;
using IAW.Testing;
using IAW.Core;
using Xunit;

namespace Core.Tests;

public class TaskLedgerTests : AgentTest<TestAgent>
{
    private ITaskLedger Ledger(string id) => Cluster.GrainFactory.GetGrain<ITaskLedger>(id);

    [Fact]
    public async Task Append_StoresEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("ledger"));

        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "0 warnings", null, DateTimeOffset.UtcNow), ct);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Single(events);
        Assert.Equal("DotNet", events[0].Agent);
        Assert.Equal(AgentEventType.BuildSucceeded, events[0].Action);
    }

    [Fact]
    public async Task GetEvents_ReturnsInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("order"));

        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted, "analyzed workspace", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "FileSystem", AgentEventType.FileCreated, "created 3 files", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "build passed", null, DateTimeOffset.UtcNow), ct);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Equal(3, events.Count);
        Assert.Equal("Roslyn", events[0].Agent);
        Assert.Equal("FileSystem", events[1].Agent);
        Assert.Equal("DotNet", events[2].Agent);
    }

    [Fact]
    public async Task GetContextBlock_FormatsCompactly()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("context"));

        await ledger.AppendAsync(new TaskEvent(
            "FileSystem", AgentEventType.FileRead, "147 transactions", "bank.csv", DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "Finance", AgentEventType.StepCompleted, "categorized into 8 groups", null, DateTimeOffset.UtcNow), ct);

        var block = await ledger.GetContextBlockAsync(maxEvents: 10, ct);

        Assert.Contains("FileSystem", block);
        Assert.Contains("Finance", block);
        Assert.Contains("147 transactions", block);
        // context block should be reasonably compact
        Assert.True(block.Length < 500, $"Context block too large: {block.Length} chars");
    }

    [Fact]
    public async Task GetContextBlock_TruncatesOldEventsWhenOverLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("truncate"));

        for (var i = 0; i < 20; i++)
        {
            await ledger.AppendAsync(new TaskEvent(
                $"Agent{i}", AgentEventType.StepCompleted, $"step {i} done", null, DateTimeOffset.UtcNow), ct);
        }

        var block = await ledger.GetContextBlockAsync(maxEvents: 5, ct);
        // should only contain the last 5 events
        Assert.Contains("Agent19", block);
        Assert.Contains("Agent15", block);
        Assert.DoesNotContain("Agent0", block);
    }

    [Fact]
    public async Task Events_SurviveGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("durable");
        var ledger = Ledger(id);

        await ledger.AppendAsync(new TaskEvent(
            "Git", AgentEventType.CommitCreated, "abc1234", "feat: add auth", DateTimeOffset.UtcNow), ct);

        await Cluster.Primary.TestHook.DeactivateAllGrainsInSilo();

        var ledger2 = Ledger(id);
        var events = await ledger2.GetEventsAsync(ct);
        Assert.Single(events);
        Assert.Equal("Git", events[0].Agent);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~TaskLedgerTests" -v n
```

Expected: FAIL — `ITaskLedger` doesn't exist.

- [ ] **Step 3: Create ITaskLedger interface**

Create `src/Core/Contracts/ITaskLedger.cs`:

```csharp
namespace Core.Contracts;

public interface ITaskLedger : IGrainWithStringKey
{
    Task AppendAsync(TaskEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<TaskEvent>> GetEventsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TaskEvent>> GetEventsSinceAsync(DateTimeOffset since, CancellationToken ct = default);
    Task<string> GetContextBlockAsync(int maxEvents = 15, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create TaskLedgerGrain implementation**

Create `src/Core/Grains/TaskLedgerGrain.cs`:

```csharp
using Core.Contracts;
using Orleans.Journaling;

namespace Core.Grains;

[GrainType(IAWConstants.GrainTypes.TaskLedger)]
public class TaskLedgerGrain(
    [FromKeyedServices("events")] IDurableList<TaskEvent> events)
    : DurableGrain, ITaskLedger
{
    public async Task AppendAsync(TaskEvent evt, CancellationToken ct = default)
    {
        events.Add(evt);
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<TaskEvent>> GetEventsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TaskEvent>>(events.ToList());

    public Task<IReadOnlyList<TaskEvent>> GetEventsSinceAsync(DateTimeOffset since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TaskEvent>>(
            events.Where(e => e.Timestamp >= since).ToList());

    public Task<string> GetContextBlockAsync(int maxEvents = 15, CancellationToken ct = default)
    {
        var recent = events.Count > maxEvents
            ? events.Skip(events.Count - maxEvents).ToList()
            : events.ToList();

        if (recent.Count == 0)
            return Task.FromResult(string.Empty);

        var lines = recent.Select(e => e.ToContextLine());
        return Task.FromResult($"[Task activity]\n{string.Join("\n", lines)}");
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        events.Clear();
        await WriteStateAsync(ct);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~TaskLedgerTests" -v n
```

Expected: PASS

- [ ] **Step 6: Build full solution**

```bash
dotnet build IAW.slnx
```

Expected: 0 errors

- [ ] **Step 7: Run all tests**

```bash
dotnet test IAW.slnx -v n
```

Expected: All pass

- [ ] **Step 8: Commit**

```bash
git add src/Core/Contracts/ITaskLedger.cs src/Core/Contracts/TaskEvent.cs src/Core/Grains/TaskLedgerGrain.cs test/Core.Tests/TaskLedgerTests.cs
git commit -m "feat: implement TaskLedger grain — shared per-task event log (#21)

- ITaskLedger grain interface with Append, GetEvents, GetContextBlock
- TaskLedgerGrain with IDurableList<TaskEvent> for crash-proof storage
- GetContextBlock returns compact text for prompt context injection
- Tests verify CRUD, ordering, truncation, and durability through deactivation"
```

---

## Task 4: Integrate Task Ledger as Context Provider (#27)

**Files:**
- Create: `src/Core/Context/TaskLedgerContextProvider.cs`
- Create: `test/Core.Tests/Context/TaskLedgerContextProviderTests.cs`
- Modify: `src/Core/Agents/Agent.cs` (add TaskId support)

- [ ] **Step 1: Write failing test for TaskLedgerContextProvider**

Create `test/Core.Tests/Context/TaskLedgerContextProviderTests.cs`:

```csharp
using Core.Context;
using Core.Contracts;
using Core.Contracts.Events;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests.Context;

public class TaskLedgerContextProviderTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetContext_ReturnsLedgerEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("ctx-task");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted, "analyzed 12 files", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "0 warnings", null, DateTimeOffset.UtcNow), ct);

        var provider = new TaskLedgerContextProvider(Cluster.GrainFactory, taskId);
        var context = await provider.GetContextAsync("test-agent", "build the project", ct);

        Assert.NotEmpty(context);
        var combined = string.Join("\n", context);
        Assert.Contains("Roslyn", combined);
        Assert.Contains("DotNet", combined);
    }

    [Fact]
    public async Task GetContext_ReturnsEmpty_WhenNoEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new TaskLedgerContextProvider(Cluster.GrainFactory, UniqueId("empty-task"));
        var context = await provider.GetContextAsync("test-agent", "hello", ct);

        Assert.Empty(context);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~TaskLedgerContextProviderTests" -v n
```

Expected: FAIL — `TaskLedgerContextProvider` doesn't exist.

- [ ] **Step 3: Create TaskLedgerContextProvider**

Create `src/Core/Context/TaskLedgerContextProvider.cs`:

```csharp
using Core.Contracts;

namespace Core.Context;

public class TaskLedgerContextProvider(IGrainFactory grainFactory, string taskId) : IAgentContextProvider
{
    public string Name => "task-ledger";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var ledger = grainFactory.GetGrain<ITaskLedger>(taskId);
            var block = await ledger.GetContextBlockAsync(maxEvents: 15, ct);

            if (string.IsNullOrEmpty(block))
                return [];

            return [block];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
```

- [ ] **Step 4: Add TaskId property to Agent base class**

In `src/Core/Agents/Agent.cs`, add after line 53 (`GetContextProviders`):

```csharp
protected string? TaskId { get; set; }
```

This allows subclasses (e.g. ThreadAgent) to set TaskId when orchestrating a task. The base class doesn't auto-register the provider — subclasses override `GetContextProviders()` and include it when they have a TaskId.

- [ ] **Step 5: Run tests**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~TaskLedgerContextProviderTests" -v n
```

Expected: PASS

- [ ] **Step 6: Build and run all tests**

```bash
dotnet build IAW.slnx && dotnet test IAW.slnx -v n
```

Expected: All pass

- [ ] **Step 7: Commit**

```bash
git add src/Core/Context/TaskLedgerContextProvider.cs src/Core/Agents/Agent.cs test/Core.Tests/Context/TaskLedgerContextProviderTests.cs
git commit -m "feat: TaskLedgerContextProvider injects ledger events as agent context (#27)

- TaskLedgerContextProvider reads task ledger and returns compact context block
- Agent base class gets TaskId property for task affiliation
- Subclasses include TaskLedgerContextProvider in GetContextProviders() when TaskId is set"
```

---

## Task 5: Integration Test — Full Event Flow

**Files:**
- Create: `test/Core.Tests/EventFlowIntegrationTests.cs`

- [ ] **Step 1: Write integration test verifying end-to-end event flow**

Create `test/Core.Tests/EventFlowIntegrationTests.cs`:

```csharp
using Core.Contracts;
using Core.Contracts.Events;
using Core.Context;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests;

public class EventFlowIntegrationTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task MultiAgent_SharesContext_ViaLedger()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("multi-agent");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        // simulate 3 agents publishing to the same task ledger
        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted,
            "analyzed workspace: 12 files, 3 interfaces", null, DateTimeOffset.UtcNow), ct);

        await ledger.AppendAsync(new TaskEvent(
            "FileSystem", AgentEventType.FileCreated,
            "created UserSettings.razor", "src/DevUI/Pages/", DateTimeOffset.UtcNow), ct);

        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded,
            "build passed, 0 warnings", null, DateTimeOffset.UtcNow), ct);

        // now a 4th agent reads the ledger as context
        var provider = new TaskLedgerContextProvider(Cluster.GrainFactory, taskId);
        var context = await provider.GetContextAsync("git-agent", "commit the changes", ct);

        Assert.NotEmpty(context);
        var block = context[0];

        // Git agent gets ALL three previous agents' activity as compact context
        Assert.Contains("Roslyn", block);
        Assert.Contains("FileSystem", block);
        Assert.Contains("DotNet", block);
        Assert.Contains("UserSettings.razor", block);

        // verify it's compact — should be well under 500 chars for 3 events
        Assert.True(block.Length < 400, $"Context block too large for 3 events: {block.Length} chars");
    }

    [Fact]
    public async Task Ledger_Events_Are_Durable()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("durable-flow");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Finance", AgentEventType.StepCompleted,
            "categorized 147 transactions", "8 categories", DateTimeOffset.UtcNow), ct);

        // deactivate all grains
        await Cluster.Primary.TestHook.DeactivateAllGrainsInSilo();

        // reactivate and verify
        var ledger2 = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var events = await ledger2.GetEventsAsync(ct);
        Assert.Single(events);
        Assert.Equal("Finance", events[0].Agent);
    }

    [Fact]
    public async Task HistoryAndLedger_BothDurable()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("both-durable");
        var taskId = UniqueId("both-task");

        // agent conversation
        var agent = Agent(agentId);
        await agent.GetResponse("Hello world", ct);

        // ledger event
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        await ledger.AppendAsync(new TaskEvent(
            "TestAgent", AgentEventType.StepCompleted, "responded to user", null, DateTimeOffset.UtcNow), ct);

        // deactivate everything
        await Cluster.Primary.TestHook.DeactivateAllGrainsInSilo();

        // verify both survived
        var agent2 = Agent(agentId);
        var history = await agent2.GetHistory(ct);
        Assert.True(history.Count >= 2);

        var ledger2 = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var events = await ledger2.GetEventsAsync(ct);
        Assert.Single(events);
    }
}
```

- [ ] **Step 2: Run integration tests**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~EventFlowIntegrationTests" -v n
```

Expected: PASS

- [ ] **Step 3: Run full test suite one final time**

```bash
dotnet test IAW.slnx -v n
```

Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add test/Core.Tests/EventFlowIntegrationTests.cs
git commit -m "test: integration tests for event-driven architecture Phase 1

- MultiAgent_SharesContext_ViaLedger: 3 agents publish, 4th reads compact context
- Ledger and history both survive grain deactivation
- Validates token-efficient context sharing (<400 chars for 3 events)"
```

---

## Task 6: Aspire Build + Run Verification

- [ ] **Step 1: Build via Aspire**

```bash
dotnet build IAW.slnx
```

- [ ] **Step 2: Start Aspire and verify via MCP**

Start the Aspire app and use MCP tools to verify:
1. Agents can still respond to messages
2. History persists correctly
3. TaskLedger grain can be created and queried (via direct grain factory call from MCP or agent tool)

- [ ] **Step 3: Final commit with any fixes from integration testing**

If any issues found during Aspire testing, fix and commit.

---

## Summary

| Task | Issue | Files Changed | Tests Added |
|------|-------|---------------|-------------|
| 1 | #20 History durability | 3 modified | 2 tests |
| 2 | #25 Typed event schema | 3 created, 1 modified | 3 tests |
| 3 | #21 Task Ledger grain | 2 created | 5 tests |
| 4 | #27 Ledger context provider | 2 created, 1 modified | 2 tests |
| 5 | Integration tests | 1 created | 3 tests |
| 6 | Aspire verification | 0 | Manual |
| **Total** | **4 issues** | **11 files** | **15 tests** |
