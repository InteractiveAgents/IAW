# Event-Driven Architecture — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add BroadcastChannel for UI notifications, Smart Event Router for zero-LLM routing, DurableTaskCompletionSource for human-in-the-loop approval gates, and PreferenceAgent for behavioral rules.

**Architecture:** Phase 2 builds on Phase 1's foundation (Task Ledger, typed events, durable history). BroadcastChannel decouples UI from publishers. EventRouter handles mechanical failures without LLM. DurableTaskCompletionSource (from Accede pattern) enables crash-proof approval gates. PreferenceAgent stores user corrections as behavioral rules injected via context providers.

**Tech Stack:** Orleans 10.0.1, Orleans.BroadcastChannel, Orleans.Journaling, .NET 11 preview, xunit.v3

---

## File Structure

### New Files
| File | Responsibility |
|------|---------------|
| `src/Core/Contracts/Notifications/UINotification.cs` | Typed notification messages for BroadcastChannel |
| `src/Core/Contracts/Notifications/IUINotificationSubscriber.cs` | BroadcastChannel subscriber interface marker |
| `src/Core/Grains/EventRouterGrain.cs` | Non-LLM grain routing critical events by pattern |
| `src/Core/Contracts/IEventRouter.cs` | EventRouter grain interface |
| `src/Core/Contracts/RoutingRule.cs` | Declarative routing rule record |
| `src/Core/Grains/ApprovalGateGrain.cs` | DurableTaskCompletionSource for human-in-the-loop |
| `src/Core/Contracts/IApprovalGate.cs` | Approval gate grain interface |
| `src/Core/Contracts/ApprovalRequest.cs` | Typed approval request/result records |
| `src/Agents/Personal/PreferenceAgent.cs` | Agent storing behavioral rules from user corrections |
| `src/Core/Contracts/IPreference.cs` | PreferenceAgent grain interface |
| `src/Core/Contracts/PreferenceRule.cs` | Typed preference rule record |
| `src/Core/Context/PreferenceContextProvider.cs` | Context provider injecting preferences |
| `test/Core.Tests/BroadcastNotificationTests.cs` | Tests for BroadcastChannel notifications |
| `test/Core.Tests/EventRouterTests.cs` | Tests for routing rules |
| `test/Core.Tests/ApprovalGateTests.cs` | Tests for approval gate durability |
| `test/Core.Tests/PreferenceAgentTests.cs` | Tests for preference storage and injection |

### Modified Files
| File | Change |
|------|--------|
| `src/Aspire.IAW.Client/IAWSiloExtensions.cs` | Add BroadcastChannel config |
| `src/IAW.Testing/AgentTest.cs` | Add BroadcastChannel to test silo |
| `src/Core/IAWConstants.cs` | Add EventRouter, ApprovalGate, Preference grain types + BroadcastChannel provider name |

---

## Task 1: BroadcastChannel for UI Notifications (#22)

**Files:**
- Create: `src/Core/Contracts/Notifications/UINotification.cs`
- Create: `test/Core.Tests/BroadcastNotificationTests.cs`
- Modify: `src/Aspire.IAW.Client/IAWSiloExtensions.cs`
- Modify: `src/IAW.Testing/AgentTest.cs`
- Modify: `src/Core/IAWConstants.cs`

- [ ] **Step 1: Write failing test for BroadcastChannel notification**

Create `test/Core.Tests/BroadcastNotificationTests.cs`:

```csharp
using Core.Contracts.Notifications;
using Xunit;

namespace Core.Tests;

public class BroadcastNotificationTests
{
    [Fact]
    public void UINotification_TaskCompleted_HasRequiredFields()
    {
        var notif = UINotification.TaskCompleted(
            taskId: "finance-march",
            summary: "Budget created. Overspending: entertainment +45%",
            filePath: "budget_march.xlsx");

        Assert.Equal("task.completed", notif.Type);
        Assert.Equal("finance-march", notif.TaskId);
        Assert.Contains("Budget created", notif.Summary);
        Assert.Equal("budget_march.xlsx", notif.FilePath);
    }

    [Fact]
    public void UINotification_Progress_HasRequiredFields()
    {
        var notif = UINotification.Progress(
            taskId: "scaffold-app",
            message: "Step 3/5: Building project...",
            percentComplete: 60);

        Assert.Equal("progress", notif.Type);
        Assert.Equal(60, notif.PercentComplete);
    }

    [Fact]
    public void UINotification_Alert_HasSeverity()
    {
        var notif = UINotification.Alert(
            severity: "critical",
            message: "API latency spike: 2340ms");

        Assert.Equal("alert", notif.Type);
        Assert.Equal("critical", notif.Severity);
    }

    [Fact]
    public void UINotification_ApprovalNeeded_HasOptions()
    {
        var notif = UINotification.ApprovalNeeded(
            approvalId: "deploy-123",
            question: "Apply fix to ReportsController?",
            options: ["Yes", "No", "Show Diff"]);

        Assert.Equal("approval", notif.Type);
        Assert.Equal(3, notif.Options!.Count);
    }
}
```

- [ ] **Step 2: Create UINotification types**

Create `src/Core/Contracts/Notifications/UINotification.cs`:

```csharp
namespace Core.Contracts.Notifications;

[GenerateSerializer]
public record UINotification(
    [property: Id(0)] string Type,
    [property: Id(1)] string? TaskId,
    [property: Id(2)] string Summary,
    [property: Id(3)] DateTimeOffset Timestamp,
    [property: Id(4)] string? FilePath = null,
    [property: Id(5)] int? PercentComplete = null,
    [property: Id(6)] string? Severity = null,
    [property: Id(7)] string? ApprovalId = null,
    [property: Id(8)] IReadOnlyList<string>? Options = null)
{
    public static UINotification TaskCompleted(string taskId, string summary, string? filePath = null)
        => new("task.completed", taskId, summary, DateTimeOffset.UtcNow, FilePath: filePath);

    public static UINotification Progress(string taskId, string message, int percentComplete)
        => new("progress", taskId, message, DateTimeOffset.UtcNow, PercentComplete: percentComplete);

    public static UINotification Alert(string severity, string message)
        => new("alert", null, message, DateTimeOffset.UtcNow, Severity: severity);

    public static UINotification ApprovalNeeded(string approvalId, string question, IReadOnlyList<string> options)
        => new("approval", null, question, DateTimeOffset.UtcNow, ApprovalId: approvalId, Options: options);
}
```

- [ ] **Step 3: Add BroadcastChannel constants to IAWConstants.cs**

Add to `IAWConstants`:
```csharp
public const string UIBroadcastProvider = "ui-notifications";
```

Add to `GrainTypes`:
```csharp
public const string EventRouter = "event-router";
public const string ApprovalGate = "approval-gate";
public const string Preference = "preference";
```

- [ ] **Step 4: Add BroadcastChannel to silo configuration**

In `src/Aspire.IAW.Client/IAWSiloExtensions.cs`, inside `builder.UseOrleans(silo => { ... })`, add:
```csharp
silo.AddBroadcastChannel(IAWConstants.UIBroadcastProvider);
```

- [ ] **Step 5: Add BroadcastChannel to test silo**

In `src/IAW.Testing/AgentTest.cs`, inside `AgentTestSiloConfigurator.Configure`, add:
```csharp
siloBuilder.AddBroadcastChannel(IAWConstants.UIBroadcastProvider);
```

- [ ] **Step 6: Run tests, build, commit**

```bash
dotnet build IAW.slnx
dotnet test test/Core.Tests --filter "FullyQualifiedName~BroadcastNotificationTests" -v n
dotnet test IAW.slnx -v n
git add -A && git commit -m "feat: add BroadcastChannel for UI notifications (#22)"
```

---

## Task 2: Smart Event Router Grain (#23)

**Files:**
- Create: `src/Core/Contracts/IEventRouter.cs`
- Create: `src/Core/Contracts/RoutingRule.cs`
- Create: `src/Core/Grains/EventRouterGrain.cs`
- Create: `test/Core.Tests/EventRouterTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/EventRouterTests.cs`:

```csharp
using Core.Contracts;
using Core.Contracts.Events;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests;

public class EventRouterTests : AgentTest<TestAgent>
{
    private IEventRouter Router() => Cluster.GrainFactory.GetGrain<IEventRouter>("global");

    [Fact]
    public async Task Route_BuildFailed_ReturnsTargetAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var router = Router();

        var result = await router.RouteAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildFailed, "CS0246: type not found", "ThemeToggle",
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("filesystem", result!.TargetAgentType);
        Assert.Equal("fix", result.Action);
    }

    [Fact]
    public async Task Route_TestFailed_ReturnsTargetAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var router = Router();

        var result = await router.RouteAsync(new TaskEvent(
            "DotNet", AgentEventType.TestFailed, "3 tests failed", null,
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("dotnet", result!.TargetAgentType);
        Assert.Equal("diagnose", result.Action);
    }

    [Fact]
    public async Task Route_HealthCritical_ReturnsEscalation()
    {
        var ct = TestContext.Current.CancellationToken;
        var router = Router();

        var result = await router.RouteAsync(new TaskEvent(
            "Aspire", AgentEventType.HealthCritical, "p99 latency 2340ms", null,
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("thread", result!.TargetAgentType);
        Assert.Equal("escalate", result.Action);
    }

    [Fact]
    public async Task Route_InfoEvent_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var router = Router();

        var result = await router.RouteAsync(new TaskEvent(
            "Git", AgentEventType.CommitCreated, "abc1234", null,
            DateTimeOffset.UtcNow), ct);

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Create contracts**

Create `src/Core/Contracts/RoutingRule.cs`:
```csharp
namespace Core.Contracts;

[GenerateSerializer]
public record RoutingRule(
    [property: Id(0)] string EventAction,
    [property: Id(1)] string TargetAgentType,
    [property: Id(2)] string Action,
    [property: Id(3)] string? ErrorCodePattern = null);

[GenerateSerializer]
public record RoutingResult(
    [property: Id(0)] string TargetAgentType,
    [property: Id(1)] string Action,
    [property: Id(2)] string? Context = null);
```

Create `src/Core/Contracts/IEventRouter.cs`:
```csharp
namespace Core.Contracts;

public interface IEventRouter : IGrainWithStringKey
{
    Task<RoutingResult?> RouteAsync(TaskEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<RoutingRule>> GetRulesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create EventRouterGrain**

Create `src/Core/Grains/EventRouterGrain.cs`:
```csharp
using Core.Contracts;
using Core.Contracts.Events;

namespace Core.Grains;

[GrainType(IAWConstants.GrainTypes.EventRouter)]
public class EventRouterGrain : Grain, IEventRouter
{
    private static readonly List<RoutingRule> Rules =
    [
        new(AgentEventType.BuildFailed, "filesystem", "fix", "CS0246"),
        new(AgentEventType.BuildFailed, "roslyn", "analyze"),
        new(AgentEventType.TestFailed, "dotnet", "diagnose"),
        new(AgentEventType.ValidationFailed, "code-orchestrator", "retry"),
        new(AgentEventType.HealthCritical, "thread", "escalate"),
        new(AgentEventType.HealthWarning, "aspire", "investigate"),
        new(AgentEventType.DeployFailed, "thread", "escalate"),
    ];

    public Task<RoutingResult?> RouteAsync(TaskEvent evt, CancellationToken ct = default)
    {
        foreach (var rule in Rules)
        {
            if (rule.EventAction != evt.Action)
                continue;

            if (rule.ErrorCodePattern is not null
                && evt.Result.Contains(rule.ErrorCodePattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<RoutingResult?>(
                    new RoutingResult(rule.TargetAgentType, rule.Action, evt.Result));
            }

            if (rule.ErrorCodePattern is null)
            {
                return Task.FromResult<RoutingResult?>(
                    new RoutingResult(rule.TargetAgentType, rule.Action, evt.Result));
            }
        }

        return Task.FromResult<RoutingResult?>(null);
    }

    public Task<IReadOnlyList<RoutingRule>> GetRulesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RoutingRule>>(Rules);
}
```

- [ ] **Step 4: Run tests, build, commit**

```bash
dotnet build IAW.slnx
dotnet test test/Core.Tests --filter "FullyQualifiedName~EventRouterTests" -v n
dotnet test IAW.slnx -v n
git add -A && git commit -m "feat: implement Smart Event Router grain — zero-LLM routing (#23)"
```

---

## Task 3: DurableTaskCompletionSource for Approval Gates (#26)

**Files:**
- Create: `src/Core/Contracts/IApprovalGate.cs`
- Create: `src/Core/Contracts/ApprovalRequest.cs`
- Create: `src/Core/Grains/ApprovalGateGrain.cs`
- Create: `test/Core.Tests/ApprovalGateTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/ApprovalGateTests.cs`:

```csharp
using Core.Contracts;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests;

public class ApprovalGateTests : AgentTest<TestAgent>
{
    private IApprovalGate Gate(string id) => Cluster.GrainFactory.GetGrain<IApprovalGate>(id);

    [Fact]
    public async Task RequestAndApprove_ReturnsResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = Gate(UniqueId("approve"));

        await gate.RequestAsync(new ApprovalRequest(
            "deploy-fix", "Apply N+1 query fix to ReportsController?",
            ["Yes", "No", "Show Diff"], "test-agent"), ct);

        var pending = await gate.GetPendingAsync(ct);
        Assert.Single(pending);
        Assert.Equal("deploy-fix", pending[0].Id);

        await gate.ResolveAsync("deploy-fix", new ApprovalDecision("Yes", "looks good"), ct);

        var result = await gate.GetResultAsync("deploy-fix", ct);
        Assert.NotNull(result);
        Assert.Equal("Yes", result!.Choice);
    }

    [Fact]
    public async Task AwaitApproval_BlocksUntilResolved()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = Gate(UniqueId("await"));

        await gate.RequestAsync(new ApprovalRequest(
            "risky-op", "Delete production branch?",
            ["Yes", "No"], "test-agent"), ct);

        // start waiting in background
        var awaitTask = gate.AwaitDecisionAsync("risky-op", ct);
        Assert.False(awaitTask.IsCompleted);

        // resolve
        await gate.ResolveAsync("risky-op", new ApprovalDecision("No", "too risky"), ct);

        var result = await awaitTask;
        Assert.Equal("No", result.Choice);
        Assert.Equal("too risky", result.Notes);
    }

    [Fact]
    public async Task Approval_SurvivesGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("durable-gate");
        var gate = Gate(id);

        await gate.RequestAsync(new ApprovalRequest(
            "deploy-v2", "Deploy version 2?", ["Yes", "No"], "deployer"), ct);

        // deactivate
        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(500, ct);

        // reactivate and check pending survives
        var gate2 = Gate(id);
        var pending = await gate2.GetPendingAsync(ct);
        Assert.Single(pending);
        Assert.Equal("deploy-v2", pending[0].Id);

        // resolve after reactivation
        await gate2.ResolveAsync("deploy-v2", new ApprovalDecision("Yes", "approved"), ct);
        var result = await gate2.GetResultAsync("deploy-v2", ct);
        Assert.Equal("Yes", result!.Choice);
    }
}
```

- [ ] **Step 2: Create contracts**

Create `src/Core/Contracts/ApprovalRequest.cs`:
```csharp
namespace Core.Contracts;

[GenerateSerializer]
public record ApprovalRequest(
    [property: Id(0)] string Id,
    [property: Id(1)] string Question,
    [property: Id(2)] IReadOnlyList<string> Options,
    [property: Id(3)] string RequestedBy,
    [property: Id(4)] DateTimeOffset Timestamp = default)
{
    public ApprovalRequest(string id, string question, IReadOnlyList<string> options, string requestedBy)
        : this(id, question, options, requestedBy, DateTimeOffset.UtcNow) { }
}

[GenerateSerializer]
public record ApprovalDecision(
    [property: Id(0)] string Choice,
    [property: Id(1)] string? Notes = null,
    [property: Id(2)] DateTimeOffset Timestamp = default)
{
    public ApprovalDecision(string choice, string? notes = null)
        : this(choice, notes, DateTimeOffset.UtcNow) { }
}
```

Create `src/Core/Contracts/IApprovalGate.cs`:
```csharp
namespace Core.Contracts;

public interface IApprovalGate : IGrainWithStringKey
{
    Task RequestAsync(ApprovalRequest request, CancellationToken ct = default);
    Task ResolveAsync(string requestId, ApprovalDecision decision, CancellationToken ct = default);
    Task<ApprovalDecision?> GetResultAsync(string requestId, CancellationToken ct = default);
    Task<ApprovalDecision> AwaitDecisionAsync(string requestId, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create ApprovalGateGrain**

Create `src/Core/Grains/ApprovalGateGrain.cs`:
```csharp
using Core.Contracts;
using Orleans.Journaling;

namespace Core.Grains;

[GrainType(IAWConstants.GrainTypes.ApprovalGate)]
public class ApprovalGateGrain(
    [FromKeyedServices("requests")] IDurableDictionary<string, ApprovalRequest> requests,
    [FromKeyedServices("decisions")] IDurableDictionary<string, ApprovalDecision> decisions)
    : DurableGrain, IApprovalGate
{
    private readonly Dictionary<string, TaskCompletionSource<ApprovalDecision>> _waiters = [];

    public async Task RequestAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        requests[request.Id] = request;
        await WriteStateAsync(ct);
    }

    public async Task ResolveAsync(string requestId, ApprovalDecision decision, CancellationToken ct = default)
    {
        decisions[requestId] = decision;
        requests.Remove(requestId);
        await WriteStateAsync(ct);

        if (_waiters.TryGetValue(requestId, out var tcs))
        {
            tcs.TrySetResult(decision);
            _waiters.Remove(requestId);
        }
    }

    public Task<ApprovalDecision?> GetResultAsync(string requestId, CancellationToken ct = default)
    {
        decisions.TryGetValue(requestId, out var decision);
        return Task.FromResult(decision);
    }

    public Task<ApprovalDecision> AwaitDecisionAsync(string requestId, CancellationToken ct = default)
    {
        // if already resolved, return immediately
        if (decisions.TryGetValue(requestId, out var existing))
            return Task.FromResult(existing);

        // otherwise create a waiter
        var tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[requestId] = tcs;

        ct.Register(() => tcs.TrySetCanceled());

        return tcs.Task;
    }

    public Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ApprovalRequest>>(requests.Values.ToList());
}
```

- [ ] **Step 4: Run tests, build, commit**

```bash
dotnet build IAW.slnx
dotnet test test/Core.Tests --filter "FullyQualifiedName~ApprovalGateTests" -v n
dotnet test IAW.slnx -v n
git add -A && git commit -m "feat: implement approval gates with DurableTaskCompletionSource (#26)"
```

---

## Task 4: PreferenceAgent — Behavioral Rules (#28)

**Files:**
- Create: `src/Core/Contracts/IPreference.cs`
- Create: `src/Core/Contracts/PreferenceRule.cs`
- Create: `src/Agents/Personal/PreferenceAgent.cs`
- Create: `src/Core/Context/PreferenceContextProvider.cs`
- Create: `test/Core.Tests/PreferenceAgentTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/PreferenceAgentTests.cs`:

```csharp
using Core.Contracts;
using Core.Context;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests;

public class PreferenceAgentTests : AgentTest<PreferenceAgent>
{
    private IPreference Pref(string id) => (IPreference)Agent(id);

    [Fact]
    public async Task SetAndGetPreference()
    {
        var ct = TestContext.Current.CancellationToken;
        var pref = Pref(UniqueId("set-get"));

        await pref.SetRuleAsync(new PreferenceRule(
            "testing", "No mocks in integration tests",
            "Past incident: mock/prod divergence", "high"), ct);

        var rules = await pref.GetRulesAsync("testing", ct);
        Assert.Single(rules);
        Assert.Equal("No mocks in integration tests", rules[0].Rule);
    }

    [Fact]
    public async Task GetRulesByCategory_FiltersCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var pref = Pref(UniqueId("filter"));

        await pref.SetRuleAsync(new PreferenceRule("testing", "No mocks", "incident", "high"), ct);
        await pref.SetRuleAsync(new PreferenceRule("architecture", "Prefer Cosmos", "latency", "high"), ct);
        await pref.SetRuleAsync(new PreferenceRule("testing", "Use real DB", "reliability", "medium"), ct);

        var testingRules = await pref.GetRulesAsync("testing", ct);
        Assert.Equal(2, testingRules.Count);

        var archRules = await pref.GetRulesAsync("architecture", ct);
        Assert.Single(archRules);
    }

    [Fact]
    public async Task Preferences_SurviveGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("durable-pref");
        var pref = Pref(id);

        await pref.SetRuleAsync(new PreferenceRule(
            "style", "No summary comments", "project convention", "high"), ct);

        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(500, ct);

        var pref2 = Pref(id);
        var rules = await pref2.GetRulesAsync("style", ct);
        Assert.Single(rules);
        Assert.Equal("No summary comments", rules[0].Rule);
    }

    [Fact]
    public async Task PreferenceContextProvider_InjectsRules()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefId = UniqueId("ctx-pref");
        var pref = Pref(prefId);

        await pref.SetRuleAsync(new PreferenceRule("testing", "No mocks in integration tests", "past incident", "high"), ct);

        var provider = new PreferenceContextProvider(Cluster.GrainFactory, prefId, "testing");
        var context = await provider.GetContextAsync("dotnet-agent", "write integration tests", ct);

        Assert.NotEmpty(context);
        var combined = string.Join("\n", context);
        Assert.Contains("No mocks", combined);
    }
}
```

- [ ] **Step 2: Create contracts**

Create `src/Core/Contracts/PreferenceRule.cs`:
```csharp
namespace Core.Contracts;

[GenerateSerializer]
public record PreferenceRule(
    [property: Id(0)] string Category,
    [property: Id(1)] string Rule,
    [property: Id(2)] string? Reason,
    [property: Id(3)] string Confidence,
    [property: Id(4)] DateTimeOffset CreatedAt = default)
{
    public PreferenceRule(string category, string rule, string? reason, string confidence)
        : this(category, rule, reason, confidence, DateTimeOffset.UtcNow) { }
}
```

Create `src/Core/Contracts/IPreference.cs`:
```csharp
namespace Core.Contracts;

public interface IPreference : IAgent
{
    Task SetRuleAsync(PreferenceRule rule, CancellationToken ct = default);
    Task RemoveRuleAsync(string category, string rule, CancellationToken ct = default);
    Task<IReadOnlyList<PreferenceRule>> GetRulesAsync(string? category = null, CancellationToken ct = default);
    Task<IReadOnlyList<PreferenceRule>> GetAllRulesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Create PreferenceAgent**

Create `src/Agents/Personal/PreferenceAgent.cs`:
```csharp
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace Agents.Personal;

public class PreferenceAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient) : Agent(durableState, chatClient), IPreference
{
    protected override string Instructions => """
        You are the Preference Agent. You store and manage user behavioral rules — corrections,
        preferences, and guidelines that modify how other agents work.

        When the user gives you a preference, store it with the appropriate category:
        - testing: test-related rules (e.g., "no mocks in integration tests")
        - architecture: architecture decisions (e.g., "prefer Cosmos for low latency")
        - style: code style rules (e.g., "no summary comments")
        - communication: how to communicate (e.g., "be concise")
        - tools: tool preferences (e.g., "use Opus for complex tasks")
        """;

    protected override int MaxHistoryMessages => 20;

    public async Task SetRuleAsync(PreferenceRule rule, CancellationToken ct = default)
    {
        var key = $"pref:{rule.Category}:{rule.Rule.GetHashCode():X8}";
        State[key] = new StateEntry(key, rule);
        await WriteStateAsync(ct);
    }

    public async Task RemoveRuleAsync(string category, string rule, CancellationToken ct = default)
    {
        var key = $"pref:{category}:{rule.GetHashCode():X8}";
        State.Remove(key);
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<PreferenceRule>> GetRulesAsync(string? category = null, CancellationToken ct = default)
    {
        var rules = State
            .Where(kvp => kvp.Key.StartsWith("pref:"))
            .Select(kvp => kvp.Value.Value)
            .OfType<PreferenceRule>()
            .Where(r => category is null || r.Category == category)
            .ToList();

        return Task.FromResult<IReadOnlyList<PreferenceRule>>(rules);
    }

    public Task<IReadOnlyList<PreferenceRule>> GetAllRulesAsync(CancellationToken ct = default)
        => GetRulesAsync(null, ct);
}
```

- [ ] **Step 4: Create PreferenceContextProvider**

Create `src/Core/Context/PreferenceContextProvider.cs`:
```csharp
using Core.Contracts;

namespace Core.Context;

public class PreferenceContextProvider(
    IGrainFactory grainFactory,
    string preferenceAgentId,
    string? categoryFilter = null) : IAgentContextProvider
{
    public string Name => "user-preferences";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var prefAgent = grainFactory.GetGrain<IPreference>(preferenceAgentId);
            var rules = await prefAgent.GetRulesAsync(categoryFilter, ct);

            if (rules.Count == 0)
                return [];

            var context = rules.Select(r =>
            {
                var reason = r.Reason is not null ? $" (reason: {r.Reason})" : "";
                return $"[preference:{r.Category}] {r.Rule}{reason}";
            }).ToList();

            return context;
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

- [ ] **Step 5: Create Personal directory if needed, run tests, build, commit**

```bash
mkdir -p src/Agents/Personal  # if doesn't exist
dotnet build IAW.slnx
dotnet test test/Core.Tests --filter "FullyQualifiedName~PreferenceAgentTests" -v n
dotnet test IAW.slnx -v n
git add -A && git commit -m "feat: add PreferenceAgent — behavioral rules that modify agent behavior (#28)"
```

---

## Task 5: Phase 2 Integration Tests

**Files:**
- Create: `test/Core.Tests/Phase2IntegrationTests.cs`

- [ ] **Step 1: Write integration tests**

Create `test/Core.Tests/Phase2IntegrationTests.cs`:

```csharp
using Core.Contracts;
using Core.Contracts.Events;
using Core.Context;
using IAW.Core;
using IAW.Testing;
using Xunit;

namespace Core.Tests;

public class Phase2IntegrationTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task EventRouter_RoutesFailure_AndLedgerTracksIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("route-flow");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var router = Cluster.GrainFactory.GetGrain<IEventRouter>("global");

        var failEvent = new TaskEvent(
            "DotNet", AgentEventType.BuildFailed, "CS0246: ThemeToggle not found", null, DateTimeOffset.UtcNow);

        // log the failure to ledger
        await ledger.AppendAsync(failEvent, ct);

        // route the failure
        var routing = await router.RouteAsync(failEvent, ct);
        Assert.NotNull(routing);
        Assert.Equal("filesystem", routing!.TargetAgentType);

        // log the routing decision
        await ledger.AppendAsync(new TaskEvent(
            "Router", AgentEventType.StepCompleted,
            $"routed to {routing.TargetAgentType}", routing.Action, DateTimeOffset.UtcNow), ct);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task ApprovalGate_FullWorkflow()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = Cluster.GrainFactory.GetGrain<IApprovalGate>(UniqueId("workflow"));

        // request approval
        await gate.RequestAsync(new ApprovalRequest(
            "deploy-1", "Deploy self-improvement fix?", ["Yes", "No"], "safe-deployer"), ct);

        // verify pending
        var pending = await gate.GetPendingAsync(ct);
        Assert.Single(pending);

        // simulate user clicking approve
        await gate.ResolveAsync("deploy-1", new ApprovalDecision("Yes", "approved by user"), ct);

        // verify resolved
        var result = await gate.GetResultAsync("deploy-1", ct);
        Assert.Equal("Yes", result!.Choice);

        // verify no longer pending
        var stillPending = await gate.GetPendingAsync(ct);
        Assert.Empty(stillPending);
    }
}
```

- [ ] **Step 2: Run tests, commit**

```bash
dotnet test test/Core.Tests --filter "FullyQualifiedName~Phase2IntegrationTests" -v n
dotnet test IAW.slnx -v n
git add -A && git commit -m "test: Phase 2 integration tests — router + approval gate workflows"
```

---

## Summary

| Task | Issue | New Files | Tests |
|------|-------|-----------|-------|
| 1 | #22 BroadcastChannel | 1 + config changes | 4 |
| 2 | #23 Smart Event Router | 3 | 4 |
| 3 | #26 Approval Gates | 3 | 3 |
| 4 | #28 PreferenceAgent | 4 | 4 |
| 5 | Integration Tests | 1 | 2 |
| **Total** | **4 issues** | **12 new files** | **17 tests** |
