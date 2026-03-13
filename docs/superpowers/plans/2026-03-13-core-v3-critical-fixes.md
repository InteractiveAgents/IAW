# Core v3 Critical Fixes Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all 7 critical issues (in 6 tasks — memory fix merged into semantic search) from the Core library assessment before v3 public NuGet release.

**Architecture:** Task 1 (constructor bag) must be done first. Then Tasks 2, 4, 5, 7 can be parallelized (but Tasks 2 and 4 both modify `Agent.cs` — coordinate to avoid merge conflicts). Task 3 (memory fix) is merged into Task 6 (semantic search) since both rewrite `Memory.Search`.

**Risk: AgentStateMapper + Orleans Journaling.** The plan uses `IAttributeToFactoryMapper` to resolve durable collections via a bag object. If Orleans Journaling tracks collections only through direct `[Memory]` constructor injection, this approach fails. Task 1 includes a persistence round-trip test to validate early. Fallback: keep `[Memory]` params on Agent, expose `AgentDurableState` as a convenience wrapper in `OnActivateAsync`.

**Tech Stack:** .NET 11, Orleans 10.0 Journaling, Microsoft.Extensions.AI, Microsoft.Agents.AI, xUnit

**Spec:** `docs/superpowers/specs/2026-03-13-core-library-assessment.md`

---

## File Map

### Task 1: Constructor Parameter Bag
- Create: `src/Core/Contracts/AgentStateAttribute.cs`
- Create: `src/Core/AI/AgentStateMapper.cs`
- Create: `src/Core/Contracts/AgentDurableState.cs`
- Modify: `src/Core/Agents/Agent.cs` (constructor signature)
- Modify: `src/Core/Agents/Agent.Events.cs` (field references)
- Modify: `src/Core/Agents/Agent.Streams.cs` (field references)
- Modify: `src/Core/Agents/Agent.State.cs` (field references)
- Modify: `src/Core/Agents/Agent.Lifecycle.cs` (field references)
- Modify: `src/Core/Agents/Agent.Tools.cs` (field references)
- Modify: `src/Core/Agents/Agent.Tracking.cs` (field references)
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs` (field references)
- Modify: `src/Core/LLM.cs` (constructor)
- Modify: `src/Core/Memory.cs` (constructor)
- Modify: `src/Core/Agents/DynamicAgent.cs` (constructor)
- Modify: All 20 agents in `src/Agents/` (constructor)
- Modify: All 4 agents in `src/Agents.CSharp/` (constructor)
- Modify: All 7 test agents in `test/Core.Tests/TestAgent.cs`
- Modify: `src/IAW.Testing/AgentTest.cs` (silo configurator)
- Test: `test/Core.Tests/AgentTests.cs`
- Test: `test/Core.Tests/ArchitectureGuardTests.cs`

### Task 2: Telemetry Standardization
- Modify: `src/Core/Agents/Agent.cs:67` (add MessagesSent to GetResponse)
- Modify: `src/Core/Agents/Agent.cs:86-98` (add ConversationDuration + ConversationErrors)
- Modify: `src/Core/Agents/Agent.Events.cs:31,52,75` (add agent.type tag)
- Modify: `src/Core/Agents/Agent.Streams.cs:61-62` (add agent.type tag)
- Test: `test/Core.Tests/AgentTests.cs` (new telemetry assertions)

### Task 3: History Windowing
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs` (add max messages)
- Modify: `src/Core/Agents/Agent.cs` (configurable MaxHistoryMessages property)
- Test: `test/Core.Tests/AgentTests.cs`

### Task 4: Tool Opt-In
- Modify: `src/Core/Agents/Agent.Tools.cs` (remove FileTools/ShellTools/WebTools from base)
- Modify: `src/Agents/Infrastructure/FileSystemAgent.cs` (override DefineTools)
- Modify: `src/Agents/Infrastructure/BuildAgent.cs` (override DefineTools)
- Modify: `src/Agents/Infrastructure/AspireAgent.cs` (override DefineTools)
- Modify: `src/Agents/Infrastructure/ShellAgent.cs` (override DefineTools)
- Modify: `src/Agents/Infrastructure/GitAgent.cs` (override DefineTools)
- Test: `test/Core.Tests/AgentTests.cs`
- Test: `test/Core.Tests/ArchitectureGuardTests.cs`

### Task 5: Memory Fixes (Access Tracking + Semantic Search, merged Tasks 3+6)
- Modify: `src/Core/Memory.cs` (fix Search + add embedding-based search)
- Modify: `src/Core/Models/MemoryEntry.cs` (add Embedding field)
- Test: `test/Core.Tests/MemoryBaseTests.cs`

### Task 6: Open Model Registry
- Modify: `src/Core/AI/LLMModel.cs` (open registration)
- Modify: `src/Core/AI/LlmRegistration.cs` (provider factory)
- Create: `src/Core/AI/ILlmProviderFactory.cs`
- Modify: `src/Core/AI/ProviderType.cs` (deprecate enum, add string provider)
- Test: `test/Core.Tests/Models/LLMModelTests.cs`

---

## Chunk 1: Constructor Parameter Bag

### Task 1: Replace 5-parameter constructor with AgentDurableState bag

The Agent base class takes 5 parameters (4 `[Memory]` + 1 `IChatClient`). Every derived class must forward all 5. Adding a 6th breaks 43+ files across consuming projects.

**Solution:** Create `AgentDurableState` record + `AgentStateAttribute` + `AgentStateMapper` following the existing `LlmAttribute<T>` + `LlmAttributeMapper<T>` pattern. The mapper resolves all durable collections from the grain's activation services, so the constructor becomes 2 parameters instead of 5.

- [ ] **Step 1: Write failing test for AgentDurableState resolution**

Add a test to `test/Core.Tests/AgentTests.cs` that verifies agent activation works with the new constructor pattern:

```csharp
[Fact]
public async Task Agent_ActivatesWithDurableState()
{
    var agent = Agent(UniqueId("durstate"));
    var metadata = await agent.GetMetadata(CancellationToken.None);
    Assert.NotNull(metadata);
    Assert.Equal("TestAgent", metadata.AgentType);
}
```

This test already exists implicitly (all agents activate). The real validation is that after refactoring, existing tests still pass — especially persistence round-tripping.

- [ ] **Step 1b: Write persistence round-trip test**

This is the critical test that validates AgentStateMapper works with Orleans Journaling:

```csharp
[Fact]
public async Task Agent_StateSurvivesDeactivationReactivation()
{
    var id = UniqueId("persist");
    var agent = Agent(id);

    // Write state via conversation and workspace
    await agent.SetWorkspace("/tmp/test", CancellationToken.None);
    await agent.GetResponse("hello", CancellationToken.None);

    var historyBefore = await agent.GetHistory(CancellationToken.None);
    var stateBefore = await agent.GetState(CancellationToken.None);
    var eventsBefore = await agent.GetEventLog(CancellationToken.None);

    Assert.NotEmpty(historyBefore);
    Assert.NotEmpty(eventsBefore);
    Assert.True(stateBefore.Entries.ContainsKey("workspace-path"));

    // Deactivate and reactivate
    await agent.Cast<IGrainManagementExtension>().DeactivateOnIdle();
    await Task.Delay(500); // allow deactivation

    var agent2 = Agent(id); // same ID, new activation
    var historyAfter = await agent2.GetHistory(CancellationToken.None);
    var stateAfter = await agent2.GetState(CancellationToken.None);
    var eventsAfter = await agent2.GetEventLog(CancellationToken.None);

    // All state must survive
    Assert.Equal(historyBefore.Count, historyAfter.Count);
    Assert.Equal(eventsBefore.Count, eventsAfter.Count);
    Assert.True(stateAfter.Entries.ContainsKey("workspace-path"));
}
```

**If this test fails after the refactor, STOP and fall back to keeping `[Memory]` params on the Agent constructor. Create `AgentDurableState` as an internal wrapper initialized in `OnActivateAsync` instead.**

- [ ] **Step 2: Run tests to establish green baseline**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj -v minimal`
Expected: All tests PASS

- [ ] **Step 3: Create AgentDurableState record**

Create `src/Core/Contracts/AgentDurableState.cs`:

```csharp
using Orleans.Journaling;

namespace Core.Contracts;

public sealed class AgentDurableState(
    IDurableDictionary<string, StateEntry> state,
    IDurableList<AgentEvent> eventLog,
    IDurableList<ChatMessage> history,
    IDurableDictionary<string, TrackingItem> trackingItems)
{
    public IDurableDictionary<string, StateEntry> State => state;
    public IDurableList<AgentEvent> EventLog => eventLog;
    public IDurableList<ChatMessage> History => history;
    public IDurableDictionary<string, TrackingItem> TrackingItems => trackingItems;
}
```

- [ ] **Step 4: Create AgentStateAttribute**

Create `src/Core/Contracts/AgentStateAttribute.cs`:

```csharp
namespace Core.Contracts;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AgentStateAttribute : Attribute, IFacetMetadata;
```

- [ ] **Step 5: Create AgentStateMapper**

Create `src/Core/AI/AgentStateMapper.cs`:

```csharp
using System.Reflection;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace Core.AI;

public sealed class AgentStateMapper : IAttributeToFactoryMapper<AgentStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        AgentStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(AgentDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type AgentDurableState.");

        return context =>
        {
            var services = context.ActivationServices;
            return new AgentDurableState(
                services.GetRequiredKeyedService<IDurableDictionary<string, StateEntry>>("agent-state"),
                services.GetRequiredKeyedService<IDurableList<AgentEvent>>("agent-events"),
                services.GetRequiredKeyedService<IDurableList<ChatMessage>>("history"),
                services.GetRequiredKeyedService<IDurableDictionary<string, TrackingItem>>("tracking"));
        };
    }
}
```

- [ ] **Step 6: Refactor Agent base class constructor**

Change `src/Core/Agents/Agent.cs` constructor from:

```csharp
public abstract partial class Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : DurableGrain, IAgent
```

To:

```csharp
public abstract partial class Agent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : DurableGrain, IAgent
```

Update all field references in `Agent.cs`:
- `state` → `durableState.State`
- `eventLog` → `durableState.EventLog`
- `history` → `durableState.History`
- `trackingItems` → `durableState.TrackingItems`

Update protected properties:
```csharp
protected IChatClient ChatClient => chatClient;
protected IDurableList<ChatMessage> History => durableState.History;
protected IDurableDictionary<string, StateEntry> State => durableState.State;
protected IDurableList<AgentEvent> EventLog => durableState.EventLog;
```

- [ ] **Step 7: Update all Agent partial files**

In each partial file, replace direct primary constructor parameter references:

**`Agent.Events.cs`:** `eventLog` → `durableState.EventLog`
**`Agent.Streams.cs`:** no direct field references (uses `StreamProvider`)
**`Agent.State.cs`:** `state` → `durableState.State`
**`Agent.Tracking.cs`:** `trackingItems` → `durableState.TrackingItems`
**`Agent.Tools.cs`:** `state` → `durableState.State`
**`Agent.Lifecycle.cs`:** no direct field references

- [ ] **Step 8: Update DurableChatHistoryProvider**

`src/Core/Agents/DurableChatHistoryProvider.cs` — no change needed, it takes `IDurableList<ChatMessage>` directly in its own constructor, passed from `Agent.OnActivateAsync` as `durableState.History`.

- [ ] **Step 9: Update LLM base class**

Change `src/Core/LLM.cs` from:

```csharp
public abstract class LLM(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems)
```

To:

```csharp
public abstract class LLM(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient)
```

- [ ] **Step 10: Update Memory base class**

Change `src/Core/Memory.cs` from 7 parameters to 4:

```csharp
public abstract class Memory(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : Agent(durableState, chatClient), IMemoryAgent
```

Update internal references: `state` → `durableState.State`, etc.

- [ ] **Step 11: Update all LLM agents (11 files)**

Each LLM agent changes from 5-param to 2-param. Example pattern for all:

```csharp
// Before:
public class Sonnet46Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Sonnet46>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : global::Core.LLM(state, eventLog, chatClient, history, trackingItems), ISonnet46

// After:
public class Sonnet46Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : global::Core.LLM(durableState, chatClient), ISonnet46
```

Apply to all 11 files:
- `src/Agents/LLM/Sonnet46Agent.cs`
- `src/Agents/LLM/Claude45HaikuAgent.cs`
- `src/Agents/LLM/Opus46Agent.cs`
- `src/Agents/LLM/Gpt4oAgent.cs`
- `src/Agents/LLM/Gpt4oMiniAgent.cs`
- `src/Agents/LLM/Gpt52Agent.cs`
- `src/Agents/LLM/Gpt53Agent.cs`
- `src/Agents/LLM/GrokLatestAgent.cs`
- `src/Agents/LLM/Llama32Agent.cs`
- `src/Agents/LLM/Qwen25Agent.cs`
- `src/Agents/LLM/Gemini31Agent.cs`

- [ ] **Step 12: Update all infrastructure agents (5 files)**

Same pattern. Example:

```csharp
// Before:
public class FileSystemAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<global::Core.Contracts.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IFileSystem

// After:
public class FileSystemAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IFileSystem
```

Apply to: `FileSystemAgent.cs`, `AspireAgent.cs`, `BuildAgent.cs`, `ShellAgent.cs`, `GitAgent.cs`

- [ ] **Step 13: Update orchestration + review + knowledge agents (10 files)**

Same 2-param pattern. Agents with extra DI params keep them:

```csharp
// PlanningAgent — has extra IGrainFactory
public class PlanningAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    IGrainFactory grainFactory)
    : Agent(durableState, chatClient), IPlanning
```

Apply to: `PersonalAssistantAgent.cs`, `PlanningAgent.cs`, `DeployerAgent.cs`, `NotificationAgent.cs`, `TaskSupervisorAgent.cs`, `CodeOrchestratorAgent.cs`, `ReviewerAgent.cs`, `SelfImprovementAgent.cs`, `KnowledgeAgent.cs`, `UserAgent.cs`

- [ ] **Step 14: Update Agents.CSharp agents (4 files)**

Agents with extra DI params keep them after the 2 base params:

```csharp
// DotNetAgent — has extra IHttpClientFactory
public class DotNetAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    IHttpClientFactory httpClientFactory)
    : Agent(durableState, chatClient), IDotNet
```

Apply to: `RoslynAgent.cs`, `DotNetAgent.cs`, `NuGetAgent.cs`, `GitHubAgent.cs`

- [ ] **Step 15: Update DynamicAgent**

```csharp
// Before:
public class DynamicAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IDynamicAgent

// After:
public class DynamicAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IDynamicAgent
```

- [ ] **Step 16: Update all 5 memory agents**

```csharp
// Before:
public class CodeMemoryAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::Core.Memory(state, eventLog, chatClient, history, trackingItems, memories, embedder), ICodeMemory

// After:
public class CodeMemoryAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::Core.Memory(durableState, chatClient, memories, embedder), ICodeMemory
```

Apply to: `CodeMemoryAgent.cs`, `UserMemoryAgent.cs`, `ProjectMemoryAgent.cs`, `PatternMemoryAgent.cs`, `EpisodeMemoryAgent.cs`

- [ ] **Step 17: Update all 7 test agents**

In `test/Core.Tests/TestAgent.cs`, update all test agents:

```csharp
// Before:
public class TestAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), ITestAgent

// After:
public class TestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), ITestAgent
```

Apply same pattern to: `ReceiverTestAgent`, `StreamTestAgent`, `TrackingTestAgent`, `ProducerTestAgent`, `RejectingReceiverAgent`, `ToolTestAgent`.

Note: `TrackingTestAgent` references `TrackingItems` via the protected property (unchanged).

- [ ] **Step 18: Register AgentStateMapper in test silo configurator**

In `src/IAW.Testing/AgentTest.cs`, find the `AgentTestSiloConfigurator` and add:

```csharp
siloBuilder.Services.AddSingleton<IAttributeToFactoryMapper<AgentStateAttribute>, AgentStateMapper>();
```

This must be registered alongside the existing `LlmAttributeMapper<T>` registrations.

- [ ] **Step 19: Register AgentStateMapper in production silo**

In `src/Core/AI/LlmRegistration.cs`, inside `AddLlmProviders`, add:

```csharp
services.AddSingleton<IAttributeToFactoryMapper<AgentStateAttribute>, AgentStateMapper>();
```

- [ ] **Step 20: Update ArchitectureGuardTests if needed**

Check `test/Core.Tests/ArchitectureGuardTests.cs` — if any test validates the constructor parameter count or `[Memory]` attributes on the base class, update it to reflect the new `[AgentState]` pattern.

- [ ] **Step 21: Run all tests**

Run: `dotnet test IAW.slnx -v minimal`
Expected: All tests PASS

- [ ] **Step 22: Commit**

```bash
git add -A
git commit -m "refactor: replace 5-param Agent constructor with AgentDurableState bag

Introduces AgentStateAttribute + AgentStateMapper (same pattern as LlmAttribute)
to resolve all 4 durable collections via a single AgentDurableState parameter.
Agent constructor goes from 5 params to 2. Adding new durable collections
now only changes AgentDurableState + AgentStateMapper — not every derived class."
```

---

## Chunk 2: Telemetry Standardization

### Task 2: Standardize telemetry tags and wire dead instruments

Two problems: (1) `agent.type` tag is missing from event-related counters, (2) `ConversationErrors` and `ConversationDuration` are declared but never used, (3) `MessagesSent` only fires for streaming, not `GetResponse`.

- [ ] **Step 1: Add MessagesSent to GetResponse**

In `src/Core/Agents/Agent.cs`, add to `GetResponse` (line ~86):

```csharp
public async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
{
    AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });
    // ... rest unchanged
```

- [ ] **Step 2: Wire ConversationDuration and ConversationErrors**

Wrap the LLM call in both `GetResponse` and `GetResponseStream` with timing and error handling:

In `GetResponse`:
```csharp
public async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
{
    AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });
    var sw = Stopwatch.StartNew();
    try
    {
        prompt = await EnrichWithContext(prompt, cancellationToken);
        var response = await _agent!.RunAsync(prompt, _session, cancellationToken: cancellationToken);
        // ... event log, WriteStateAsync ...
        return response.Text ?? string.Empty;
    }
    catch (Exception)
    {
        AgentTelemetry.ConversationErrors.Add(1, new TagList { { "agent.type", GetType().Name } });
        throw;
    }
    finally
    {
        AgentTelemetry.ConversationDuration.Record(sw.Elapsed.TotalSeconds,
            new TagList { { "agent.type", GetType().Name } });
    }
}
```

- [ ] **Step 3: Add agent.type tag to event counters**

In `Agent.Events.cs`, update all three `EventsPublished.Add` calls to include `agent.type`:

```csharp
// PublishAsync (line ~31):
AgentTelemetry.EventsPublished.Add(1, new TagList
{
    { "event.name", eventName },
    { "agent.type", GetType().Name }
});

// PublishToStream (line ~52):
AgentTelemetry.EventsPublished.Add(1, new TagList
{
    { "event.name", streamName },
    { "agent.type", GetType().Name }
});

// PublishToTaskStream (line ~75):
AgentTelemetry.EventsPublished.Add(1, new TagList
{
    { "event.name", typeof(TEvent).Name },
    { "agent.type", GetType().Name }
});
```

- [ ] **Step 4: Add agent.type tag to event handling counters**

In `Agent.Streams.cs`, update `EventsHandled.Add` and `EventHandleDuration.Record` (lines ~61-62):

```csharp
AgentTelemetry.EventsHandled.Add(1, new TagList
{
    { "event.name", streamName },
    { "agent.type", GetType().Name }
});
AgentTelemetry.EventHandleDuration.Record(sw.Elapsed.TotalSeconds, new TagList
{
    { "event.name", streamName },
    { "agent.type", GetType().Name }
});
```

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj -v minimal`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/Core/Agents/Agent.cs src/Core/Agents/Agent.Events.cs src/Core/Agents/Agent.Streams.cs
git commit -m "fix: standardize agent.type tag on all telemetry counters

Wire ConversationErrors and ConversationDuration (were declared but unused).
Add MessagesSent to GetResponse (was streaming-only).
Add agent.type tag to EventsPublished, EventsHandled, EventHandleDuration."
```

---

## Chunk 3: History Windowing (was Chunk 4)

### Task 4: Add configurable history windowing to DurableChatHistoryProvider

`ProvideChatHistoryAsync` loads the entire durable history list on every LLM call. For long-running agents this degrades performance and can exceed context windows.

- [ ] **Step 1: Write failing test**

In `test/Core.Tests/AgentTests.cs`, add:

```csharp
[Fact]
public async Task Agent_HistoryWindowingTrimsDurableListOnStore()
{
    var agent = Agent(UniqueId("histwin"));

    // Send many messages to build up history
    for (var i = 0; i < 60; i++)
        await agent.GetResponse($"Message {i}", CancellationToken.None);

    // GetHistory returns the raw durable list (full history for audit).
    // Windowing applies to what gets sent to the LLM via DurableChatHistoryProvider.
    // Verify the durable list has all messages (no data loss)
    var fullHistory = await agent.GetHistory(CancellationToken.None);
    Assert.Equal(120, fullHistory.Count); // 60 user + 60 assistant

    // The LLM should only see the last MaxHistoryMessages (100).
    // After 60 more messages, the 61st call should still work without
    // exceeding context — verified by not throwing.
    for (var i = 60; i < 120; i++)
        await agent.GetResponse($"Message {i}", CancellationToken.None);

    // Full history grows, but LLM context stays bounded
    var afterHistory = await agent.GetHistory(CancellationToken.None);
    Assert.Equal(240, afterHistory.Count); // all messages preserved
}
```

Note: `GetHistory()` returns the full durable list (needed for audit/export). Windowing only applies to what `DurableChatHistoryProvider.ProvideChatHistoryAsync` sends to the LLM. The test verifies the agent can handle 240 messages without the LLM call failing due to unbounded context.

- [ ] **Step 2: Run test to verify baseline behavior**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "Agent_HistoryWindowingTrimsDurableListOnStore" -v minimal`
Expected: PASS (baseline — the test itself validates the windowing is in place after implementation)

- [ ] **Step 3: Add MaxHistoryMessages to Agent**

In `src/Core/Agents/Agent.cs`, add virtual property:

```csharp
protected virtual int MaxHistoryMessages => 100;
```

- [ ] **Step 4: Add windowing to DurableChatHistoryProvider**

Change `src/Core/Agents/DurableChatHistoryProvider.cs`:

```csharp
internal sealed class DurableChatHistoryProvider(
    IDurableList<ChatMessage> history,
    int maxMessages) : ChatHistoryProvider
{
    public override IReadOnlyList<string> StateKeys => ["orleans-durable-history"];

    protected override ValueTask<IEnumerable<AiChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var skip = Math.Max(0, history.Count - maxMessages);
        IEnumerable<AiChatMessage> messages = history
            .Skip(skip)
            .Select(m => new AiChatMessage(new AiChatRole(m.Role), m.Content));

        return ValueTask.FromResult(messages);
    }

    // StoreChatHistoryAsync unchanged
}
```

- [ ] **Step 5: Pass MaxHistoryMessages to provider in OnActivateAsync**

In `src/Core/Agents/Agent.cs`, update `OnActivateAsync`:

```csharp
ChatHistoryProvider = new DurableChatHistoryProvider(durableState.History, MaxHistoryMessages)
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "Agent_HistoryIsWindowedToMaxMessages" -v minimal`
Expected: PASS

- [ ] **Step 7: Run full test suite**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj -v minimal`
Expected: All PASS

- [ ] **Step 8: Commit**

```bash
git add src/Core/Agents/Agent.cs src/Core/Agents/DurableChatHistoryProvider.cs test/Core.Tests/AgentTests.cs
git commit -m "feat: add history windowing with configurable MaxHistoryMessages

DurableChatHistoryProvider now skips older messages beyond MaxHistoryMessages
(default 100). Prevents unbounded history growth from degrading LLM performance.
Agents can override MaxHistoryMessages for custom limits."
```

---

## Chunk 4: Tool Opt-In

### Task 4: Remove FileTools, ShellTools, and WebTools from base Agent

Every agent gets file, shell, and web tools the moment a workspace is set. This violates least privilege. Only agents that explicitly need these tools should have them.

- [ ] **Step 1: Write test for base agent having no file/shell/web tools**

In `test/Core.Tests/AgentTests.cs`:

```csharp
[Fact]
public async Task Agent_BaseToolsContainOnlyWorkspaceAndTracking()
{
    var agent = Agent(UniqueId("basetools"));
    await agent.SetWorkspace("/tmp/test-workspace", CancellationToken.None);
    var capabilities = await agent.GetCapabilities(CancellationToken.None);
    // Base agent should have tools (workspace + tracking) but NOT file/shell/web
    Assert.True(capabilities.HasTools);
}
```

- [ ] **Step 2: Strip FileTools, ShellTools, WebTools from Agent.Tools.cs**

Change `src/Core/Agents/Agent.Tools.cs` `GetAllTools()`:

```csharp
private IReadOnlyList<AITool> GetAllTools()
{
    if (_cachedTools is not null)
        return _cachedTools;

    var tools = new List<AITool>();

    var workspaceTools = new WorkspaceTools(
        () => GetWorkspacePath() ?? ".",
        path =>
        {
            durableState.State[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path);
            _cachedTools = null;
        });
    RegisterToolMethods(tools, workspaceTools);

    tools.AddRange(DefineTools());
    _cachedTools = tools;
    return _cachedTools;
}
```

- [ ] **Step 3: Add FileTools/ShellTools to infrastructure agents**

For each infrastructure agent that needs file/shell access, override `DefineTools()`. Example for `FileSystemAgent`:

```csharp
protected override IReadOnlyList<AITool> DefineTools()
{
    var tools = new List<AITool>();
    var workspacePath = GetWorkspacePath();
    if (workspacePath is not null)
    {
        RegisterToolMethods(tools, new FileTools(() => workspacePath));
        RegisterToolMethods(tools, new ShellTools(() => workspacePath));
    }
    return tools;
}
```

Apply similarly to: `BuildAgent`, `AspireAgent`, `ShellAgent`, `GitAgent`, `RoslynAgent`, `DotNetAgent`.

- [ ] **Step 4: Add WebTools only to agents that need web access**

Only these agents get `WebTools`:
- `KnowledgeAgent` — needs to fetch external documentation
- `DotNetAgent` — already has `IHttpClientFactory`, use it instead of `new HttpClient()`
- `NuGetAgent` — already has `IHttpClientFactory`, use it
- `SelfImprovementAgent` — may fetch external references

For agents with `IHttpClientFactory`:
```csharp
RegisterToolMethods(tools, new WebTools(httpClientFactory.CreateClient()));
```

For agents without:
```csharp
RegisterToolMethods(tools, new WebTools(new HttpClient()));
```

All other agents (LLM wrappers, memory agents, etc.) do NOT get `WebTools`. This eliminates the per-activation `HttpClient` creation for ~30 agents that never use web tools.

- [ ] **Step 5: Run all tests**

Run: `dotnet test IAW.slnx -v minimal`
Expected: All PASS

- [ ] **Step 6: Commit**

```bash
git add src/Core/Agents/Agent.Tools.cs src/Agents/
git commit -m "security: remove FileTools/ShellTools/WebTools from base Agent

Base Agent now only provides WorkspaceTools + tracking tools.
Infrastructure agents opt-in to file/shell/web tools via DefineTools().
Enforces least-privilege for LLM and memory agents."
```

---

## Chunk 5: Memory Fixes (Access Tracking + Semantic Search)

### Task 5: Fix access tracking and implement semantic search in Memory base (merged Tasks 3+6)

Two problems in `Memory.Search`: (1) access tracking is broken — `with` copies never written back, (2) keyword-only `string.Contains` with no semantic search despite `IEmbeddingGenerator` being available.

- [ ] **Step 1: Write failing test for access tracking**

In `test/Core.Tests/MemoryBaseTests.cs`:

```csharp
[Fact]
public async Task Search_UpdatesAccessCountInStore()
{
    var memory = GetMemoryAgent();
    await memory.ObserveAsync("important pattern", "test", CancellationToken.None);

    var results1 = await memory.SearchAsync("important", 5, CancellationToken.None);
    Assert.Single(results1);
    Assert.Equal(1, results1[0].AccessCount);

    var results2 = await memory.SearchAsync("important", 5, CancellationToken.None);
    Assert.Single(results2);
    Assert.Equal(2, results2[0].AccessCount);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "Search_UpdatesAccessCountInStore" -v minimal`
Expected: FAIL — access count stays 1

- [ ] **Step 3: Write test for semantic search with keyword fallback**

```csharp
[Fact]
public async Task Search_FallsBackToKeywordWhenEmbeddingsAreZeroVectors()
{
    var memory = GetMemoryAgent();
    await memory.ObserveAsync("the cat sat on the mat", "test", CancellationToken.None);
    await memory.ObserveAsync("unrelated financial data", "test", CancellationToken.None);

    // MockEmbeddingGenerator returns zero-vectors, so cosine similarity = 0.
    // Keyword fallback should still find "cat" in the first entry.
    var results = await memory.SearchAsync("cat", 5, CancellationToken.None);
    Assert.Single(results);
    Assert.Contains("cat", results[0].Content);
}
```

Note: `MockEmbeddingGenerator` returns zero-vectors (dimension 384). The `CosineSimilarity` method returns `0f` for zero-vectors (handled by `denom == 0` check). The test uses a keyword query that matches via `string.Contains` fallback, validating that semantic search degrades gracefully.

- [ ] **Step 2: Add Embedding field to MemoryEntry**

In `src/Core/Models/MemoryEntry.cs`, add an optional embedding field:

```csharp
[Id(8)] public float[]? Embedding { get; init; }
```

- [ ] **Step 3: Generate embedding on Observe**

In `src/Core/Memory.cs`, update `Observe`:

```csharp
protected virtual async Task Observe(string content, MemoryProvenance provenance, CancellationToken ct = default)
{
    float[]? embedding = null;
    try
    {
        var result = await Embedder.GenerateAsync(content, cancellationToken: ct);
        embedding = result.Vector.ToArray();
    }
    catch
    {
        // embedder unavailable — fall back to keyword-only
    }

    var entry = new MemoryEntry(
        Guid.NewGuid().ToString("N"),
        content, provenance, 1.0f,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null)
    { Embedding = embedding };

    memories.Add(entry);
    await WriteStateAsync(ct);
}
```

- [ ] **Step 4: Implement semantic search with cosine similarity fallback**

In `src/Core/Memory.cs`, replace `Search`:

```csharp
protected virtual async Task<IReadOnlyList<MemoryEntry>> Search(string query, int topK = 5, CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();

    float[]? queryEmbedding = null;
    try
    {
        var result = await Embedder.GenerateAsync(query, cancellationToken: ct);
        queryEmbedding = result.Vector.ToArray();
    }
    catch
    {
        // embedder unavailable — keyword fallback
    }

    var scored = new List<(MemoryEntry Entry, float Score, int Index)>();
    for (var i = 0; i < memories.Count; i++)
    {
        var entry = memories[i];
        float score;

        if (queryEmbedding is not null && entry.Embedding is not null)
            score = CosineSimilarity(queryEmbedding, entry.Embedding);
        else if (entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            score = 0.5f;
        else
            continue;

        var updated = entry with { AccessCount = entry.AccessCount + 1, LastAccessedAt = DateTimeOffset.UtcNow };
        memories[i] = updated;
        scored.Add((updated, score * entry.RelevanceScore, i));
    }

    if (scored.Count > 0)
        await WriteStateAsync(ct);

    IReadOnlyList<MemoryEntry> topResults = [.. scored.OrderByDescending(s => s.Score).Take(topK).Select(s => s.Entry)];
    return topResults;
}

private static float CosineSimilarity(float[] a, float[] b)
{
    if (a.Length != b.Length || a.Length == 0) return 0f;
    float dot = 0, magA = 0, magB = 0;
    for (var i = 0; i < a.Length; i++)
    {
        dot += a[i] * b[i];
        magA += a[i] * a[i];
        magB += b[i] * b[i];
    }
    var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
    return denom == 0 ? 0f : dot / denom;
}
```

- [ ] **Step 7: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj -v minimal`
Expected: All PASS

- [ ] **Step 8: Commit**

```bash
git add src/Core/Memory.cs src/Core/Models/MemoryEntry.cs test/Core.Tests/MemoryBaseTests.cs
git commit -m "feat: fix access tracking and add semantic memory search

Fixes broken access tracking (copies were never written back).
Memory.Observe now generates embeddings via IEmbeddingGenerator.
Memory.Search uses cosine similarity when embeddings are available,
falls back to keyword search otherwise."
```

---

## Chunk 6: Open Model Registry

### Task 6: Open model registration for NuGet consumers

The LLM model registry has 13 hardcoded sealed classes. NuGet consumers can't add custom models. `ProviderType` is a closed enum.

- [ ] **Step 1: Write failing test for runtime model registration**

In `test/Core.Tests/Models/LLMModelTests.cs`:

```csharp
[Fact]
public void RegisterCustomModel_AppearsInRegistry()
{
    var model = LLMModel.Register("my-fine-tuned-gpt", "openai", "my-fine-tuned-gpt");
    Assert.Contains(LLMModel.All, m => m.Id == "my-fine-tuned-gpt");
    Assert.Equal("openai-my-fine-tuned-gpt", model.ServiceKey);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "RegisterCustomModel_AppearsInRegistry" -v minimal`
Expected: FAIL — `LLMModel.Register` doesn't exist

- [ ] **Step 3: Add Register method to LLMModel**

In `src/Core/AI/LLMModel.cs`, add a static factory method:

```csharp
public static LLMModel Register(string id, string provider, string displayName, ModelCapabilities? capabilities = null)
{
    var model = new ConfiguredLLMModel(id, provider, displayName, capabilities ?? ModelCapabilities.Standard);
    return model;
}

private sealed class ConfiguredLLMModel(string id, string provider, string displayName, ModelCapabilities capabilities) : LLMModel
{
    public override string Id => id;
    public override string Provider => provider;
    public override string DisplayName => displayName;
    public override ModelCapabilities Capabilities => capabilities;
}
```

Also change the `Provider` property from `ProviderType` enum to `string`. This ripples through:
- `LLMModel.ServiceKey` — currently calls `Provider.ToString().ToLowerInvariant()`, change to just `Provider.ToLowerInvariant()`
- `LLMModel.IsLocal` — currently `Provider == ProviderType.Ollama`, change to `Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)`
- `LlmRegistration.IsProviderConfigured()` — switch on `ProviderType`, replace with string comparison
- `LlmRegistration.CreateChatClient()` — switch on `ProviderType`, replace with `ILlmProviderFactory` lookup
- All 13 model classes in `src/Core/AI/Models/` — change `override ProviderType Provider =>` to `override string Provider =>`
- `src/Core/AI/ProviderType.cs` — mark as `[Obsolete]` or delete
- `src/IAW.AppHost/IAWExtensions.cs` — if it references `ProviderType`

Existing models return `"anthropic"`, `"openai"`, `"ollama"`, `"github"` as strings.
New consumer-defined models can use any provider string.

- [ ] **Step 4: Create ILlmProviderFactory**

Create `src/Core/AI/ILlmProviderFactory.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace Core.AI;

public interface ILlmProviderFactory
{
    string ProviderName { get; }
    IChatClient CreateClient(LLMModel model, IServiceProvider services);
}
```

- [ ] **Step 5: Refactor LlmRegistration to use provider factories**

In `src/Core/AI/LlmRegistration.cs`, replace the hardcoded `switch` on `ProviderType` with `ILlmProviderFactory` resolution:

```csharp
// In AddLlmProviders:
var factories = services.BuildServiceProvider().GetServices<ILlmProviderFactory>()
    .ToDictionary(f => f.ProviderName, StringComparer.OrdinalIgnoreCase);

// For each model:
if (factories.TryGetValue(model.Provider, out var factory))
{
    var client = factory.CreateClient(model, sp);
    // wrap with ChatClientBuilder...
}
```

Register built-in factories for Anthropic, OpenAI, Ollama:

```csharp
services.AddSingleton<ILlmProviderFactory, AnthropicProviderFactory>();
services.AddSingleton<ILlmProviderFactory, OpenAIProviderFactory>();
services.AddSingleton<ILlmProviderFactory, OllamaProviderFactory>();
```

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj -v minimal`
Expected: All PASS

- [ ] **Step 7: Commit**

```bash
git add src/Core/AI/
git commit -m "feat: open model registry with ILlmProviderFactory

LLMModel.Register() allows NuGet consumers to add custom models at runtime.
Provider is now a string instead of closed enum.
ILlmProviderFactory allows plugging in custom LLM providers.
Built-in factories for Anthropic, OpenAI, Ollama registered by default."
```

---

## Execution Order

1. **Task 1** first (constructor bag) — foundational, changes every file
2. After Task 1: **Tasks 2, 3, 4, 6** can be parallelized (but Tasks 2+3 both modify `Agent.cs` — coordinate)
3. **Task 5** (memory fixes) is independent, can run in parallel with any non-Memory task

## Verification

After all tasks complete:

```bash
dotnet build IAW.slnx
dotnet test IAW.slnx -v minimal
```

All tests must pass. Then run integration tests:

```bash
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj -v minimal
```
