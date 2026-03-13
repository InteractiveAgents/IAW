# IAW v0.2.0 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform IAW from a basic agent framework into a fully orchestrated, memory-aware, multi-model runtime with typed code generation, consilium patterns, and durable task recovery.

**Architecture:** Layered build — foundation types first, then communication refactor, then InterfaceCatalog, then LLM/Memory agents, then code orchestration, then persistence, then docs. Each layer is independently testable. All changes target the opensource repo at `E:\IAW\InteractiveAgents`.

**Tech Stack:** .NET 11, Orleans 10.0, Aspire 13.1, Microsoft.Extensions.AI, Roslyn 5.0, Qdrant, ElBruno.LocalEmbeddings, CosmosDB Emulator

**Spec:** `docs/specs/2026-03-11-iaw-v020-design.md`

---

## Chunk 1: Foundation — Types, Agent Base Changes, Grain ID Convention

### Task 1: Define ITaskStreamEvent and typed task stream events

**Important:** `IEvent` already exists at `src/Core/Messages/IEvent.cs` and extends `IAgentMessage` (which requires `SourceAgentId`, `CorrelationId`, `Timestamp`). Do NOT create a new `IEvent`. All new event types must extend the existing `Core.Messages.IEvent`.

**Files:**
- Create: `src/Core/Messages/ITaskStreamEvent.cs`
- Create: `src/Core/Messages/Events/StepProgressEvent.cs`
- Create: `src/Core/Messages/Events/StepCompletedEvent.cs`
- Create: `src/Core/Messages/Events/StepFailedEvent.cs`
- Create: `src/Core/Messages/Events/TaskCompletedEvent.cs`
- Create: `src/Core/Messages/Events/ConsiliumResponseEvent.cs`
- Read: `src/Core/Messages/IEvent.cs` and `src/Core/Messages/IAgentMessage.cs` first
- Test: `test/Core.Tests/Communication/EventTypeTests.cs`

- [ ] **Step 1: Read existing IEvent and IAgentMessage interfaces**

Existing hierarchy:
```csharp
// src/Core/Messages/IAgentMessage.cs
public interface IAgentMessage
{
    string SourceAgentId { get; }
    string CorrelationId { get; }
    DateTimeOffset Timestamp { get; }
}

// src/Core/Messages/IEvent.cs
public interface IEvent : IAgentMessage;
```

- [ ] **Step 2: Write tests for event serialization**

```csharp
public class EventTypeTests
{
    [Fact]
    public void StepProgressEvent_implements_ITaskStreamEvent_and_IEvent()
    {
        ITaskStreamEvent evt = new StepProgressEvent("agent-1", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow,
            "task-1", "analyzing", null);
        Assert.Equal("task-1", evt.TaskId);
        Assert.IsAssignableFrom<IEvent>(evt);
        Assert.IsAssignableFrom<IAgentMessage>(evt);
    }

    [Fact]
    public void All_task_stream_events_implement_IEvent_and_IAgentMessage()
    {
        var types = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ITaskStreamEvent)) && !t.IsInterface);
        Assert.All(types, t =>
        {
            Assert.True(t.IsAssignableTo(typeof(IEvent)));
            Assert.True(t.IsAssignableTo(typeof(IAgentMessage)));
        });
    }

    [Fact]
    public void All_task_stream_events_have_GenerateSerializer()
    {
        var types = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ITaskStreamEvent)) && !t.IsInterface);
        Assert.All(types, t =>
            Assert.NotNull(t.GetCustomAttribute<GenerateSerializerAttribute>()));
    }
}
```

- [ ] **Step 3: Run tests — verify they fail (types don't exist yet)**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~EventTypeTests" -v n`
Expected: FAIL — types not found

- [ ] **Step 4: Implement ITaskStreamEvent and all event records**

`src/Core/Messages/ITaskStreamEvent.cs`:
```csharp
namespace Core.Messages;

public interface ITaskStreamEvent : IEvent
{
    string TaskId { get; }
}
```

Note: `ITaskStreamEvent` extends `IEvent` which extends `IAgentMessage`. `AgentId` maps to `SourceAgentId`, `Timestamp` is inherited from `IAgentMessage`. Only `TaskId` is new.

`src/Core/Messages/Events/StepProgressEvent.cs`:
```csharp
namespace Core.Messages.Events;

[GenerateSerializer]
public record StepProgressEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string TaskId,
    [property: Id(4)] string StepDescription,
    [property: Id(5)] string? Output) : ITaskStreamEvent;
```

Follow same pattern for `StepCompletedEvent`, `StepFailedEvent`, `TaskCompletedEvent`, `ConsiliumResponseEvent` per spec Section 2. Each must include `SourceAgentId`, `CorrelationId`, `Timestamp` (from IAgentMessage) plus `TaskId` (from ITaskStreamEvent) plus event-specific fields.

- [ ] **Step 5: Run tests — verify they pass**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~EventTypeTests" -v n`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Core/Messages/ITaskStreamEvent.cs src/Core/Messages/Events/ test/Core.Tests/Communication/EventTypeTests.cs
git commit -m "feat: add ITaskStreamEvent and typed task stream events"
```

---

### Task 2: Define StepRecord, StepResult, and orchestration types

**Files:**
- Create: `src/Core/Orchestration/StepRecord.cs`
- Create: `src/Core/Orchestration/StepResult.cs`
- Create: `src/Core/Orchestration/OrchestrationStatus.cs`
- Test: `test/Core.Tests/Orchestration/OrchestrationTypesTests.cs`

- [ ] **Step 1: Write tests for orchestration types**

```csharp
public class OrchestrationTypesTests
{
    [Fact]
    public void StepRecord_roundtrips_serialization()
    {
        var record = new StepRecord(0, "roslyn", "analyze", StepStatus.Pending, new() { ["path"] = "src/" });
        Assert.Equal("roslyn", record.AgentId);
        Assert.Equal(StepStatus.Pending, record.Status);
    }

    [Fact]
    public void StepResult_stores_duration_and_output()
    {
        var result = new StepResult("Build succeeded", TimeSpan.FromSeconds(12), "dot-net", DateTimeOffset.UtcNow);
        Assert.Equal("Build succeeded", result.Output);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement types per spec Section 4**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Orchestration/StepRecord.cs src/Core/Orchestration/StepResult.cs src/Core/Orchestration/OrchestrationStatus.cs test/Core.Tests/Orchestration/OrchestrationTypesTests.cs
git commit -m "feat: add StepRecord, StepResult, OrchestrationStatus types"
```

---

### Task 3: Define MemoryEntry and MemoryProvenance types

**Files:**
- Create: `src/Core/Models/MemoryEntry.cs`
- Create: `src/Core/Models/MemoryProvenance.cs`
- Test: `test/Core.Tests/Models/MemoryEntryTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class MemoryEntryTests
{
    [Fact]
    public void MemoryProvenance_trust_scores_are_valid()
    {
        var provenance = new MemoryProvenance("user-input", null, null, null, DateTimeOffset.UtcNow, null, 1.0f);
        Assert.Equal(1.0f, provenance.TrustScore);
    }

    [Fact]
    public void MemoryEntry_tracks_supersession()
    {
        var entry = new MemoryEntry("id-1", "user likes tabs",
            new MemoryProvenance("user-input", null, null, null, DateTimeOffset.UtcNow, null, 1.0f),
            1.0f, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, "id-2");
        Assert.Equal("id-2", entry.SupersededBy);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement MemoryEntry and MemoryProvenance per spec Section 5**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Models/MemoryEntry.cs src/Core/Models/MemoryProvenance.cs test/Core.Tests/Models/MemoryEntryTests.cs
git commit -m "feat: add MemoryEntry and MemoryProvenance types with trust scores"
```

---

### Task 4: Simplify Agent base constructor (5 params -> 3)

**Files:**
- Modify: `src/Core/Agent/Agent.cs` — change constructor signature
- Modify: `src/Core/Agent/DynamicAgent.cs` — update constructor call
- Modify: ALL agent subclasses in `src/Agents/` — update constructor calls
- Modify: `src/IAW.Testing/AgentTest.cs` — update test infrastructure
- Test: `test/Core.Tests/` — existing tests must still pass

- [ ] **Step 1: Read current Agent.cs constructor and all derived agent constructors**

Key file: `src/Core/Agent/Agent.cs`. Identify every class that calls `Agent(state, eventLog, chatClient, history, trackingItems)`.

- [ ] **Step 2: Modify Agent.cs — internalize history and trackingItems**

The Agent base class manages `[Memory("history")]` and `[Memory("tracking")]` internally. Derived classes only pass `state`, `eventLog`, `chatClient`.

- [ ] **Step 3: Update all agent subclass constructors**

Update every agent in:
- `src/Agents/Infrastructure/` (FileSystemAgent, ShellAgent, GitAgent, BuildAgent, AspireAgent)
- `src/Agents/Orchestration/` (PersonalAssistantAgent, PlanningAgent, DeployerAgent, NotificationAgent)
- `src/Agents/Review/` (ReviewerAgent, SelfImprovementAgent)
- `src/Agents/Knowledge/` (KnowledgeAgent, UserAgent)
- `src/Agents.CSharp/` (RoslynAgent, DotNetAgent, NuGetAgent, GitHubAgent)
- `src/Core/Agent/DynamicAgent.cs`

- [ ] **Step 4: Update AgentTest infrastructure**

Modify `src/IAW.Testing/AgentTest.cs` and configurators to match new constructor.
**Also add `RegisterLlmMapper` calls for ALL new models** (Opus46, Gpt52, Gpt53, Gemini31, GrokLatest) so tests can resolve them.

- [ ] **Step 5: Run ALL tests**

Run: `dotnet test IAW.slnx -v n`
Expected: ALL PASS — this is a refactor, behavior unchanged

- [ ] **Step 6: Commit**

```bash
git add src/Core/Agent/ src/Agents/ src/Agents.CSharp/ src/IAW.Testing/
git commit -m "refactor: simplify Agent constructor from 5 to 3 params"
```

---

### Task 5: Add PublishToTaskStream and PublishToStream\<T\> methods

**Important:** `Agent.Events.cs` already has `PublishTypedAsync<TEvent>`. This must be reconciled — either rename or replace. Also, `Agent.Streams.cs` currently routes all stream subscriptions through `HandleEvent(AgentEvent)` — this dispatch path must be rewired to call typed `OnStreamEventAsync` directly.

**Files:**
- Modify: `src/Core/Agent/Agent.Events.cs` — add typed publish methods, remove `PublishAsync(string, dict)`, reconcile with existing `PublishTypedAsync`
- Modify: `src/Core/Agent/Agent.Streams.cs` — rewire dispatch from `HandleEvent` to typed `OnStreamEventAsync`
- Modify: `src/Core/Contracts/IAgent.cs` — remove `HandleEvent`, remove `PublishToStream(AgentEvent)`
- Modify: `src/IAW.MCP/Tools/AgentTools.cs` — remove any calls to HandleEvent
- Modify: `src/Agents/Orchestration/NotificationAgent.cs` — replace HandleEvent override with IStreamConsumer<T>
- Test: `test/Core.Tests/Communication/TypedPublishTests.cs`

- [ ] **Step 1: Write tests for typed publishing**

```csharp
public class TypedPublishTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task PublishToTaskStream_adds_to_event_log()
    {
        var agent = Agent("test-publish");
        // Agent should have method to publish typed events to task stream
        // Event should be auto-logged
        var log = await agent.GetEventLog(default);
        Assert.Contains(log, e => e.EventName.Contains("step.progress"));
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement typed publish methods on Agent.Events.cs**

```csharp
protected async Task PublishToTaskStream<TEvent>(string taskId, TEvent evt) where TEvent : IEvent
{
    var streamId = StreamId.Create("agents", $"task/{taskId}");
    var stream = this.GetStreamProvider("agents").GetStream<TEvent>(streamId);
    await stream.OnNextAsync(evt);
    // auto-log
    await eventLog.AddAsync(new AgentEvent(
        typeof(TEvent).Name,
        DateTimeOffset.UtcNow,
        new Dictionary<string, object> { ["taskId"] = taskId }));
}

protected async Task PublishToStream<TEvent>(TEvent evt) where TEvent : IEvent
{
    var streamName = EventTypeToStreamName(typeof(TEvent).Name);
    var streamId = StreamId.Create("agents", streamName);
    var stream = this.GetStreamProvider("agents").GetStream<TEvent>(streamId);
    await stream.OnNextAsync(evt);
    // auto-log
    await eventLog.AddAsync(new AgentEvent(
        typeof(TEvent).Name, DateTimeOffset.UtcNow, new()));
}
```

- [ ] **Step 4: Remove HandleEvent and PublishToStream(AgentEvent) from IAgent interface at `src/Core/Contracts/IAgent.cs`**
- [ ] **Step 5: Remove or rename existing `PublishTypedAsync<TEvent>` in Agent.Events.cs — consolidate into `PublishToStream<TEvent>`**
- [ ] **Step 6: Remove old `PublishAsync(string eventName, Dictionary<string, object> payload)` from Agent.Events.cs**
- [ ] **Step 7: Rewire Agent.Streams.cs dispatch — replace `HandleEvent(evt)` call with typed `OnStreamEventAsync` invocation on concrete `IStreamConsumer<TEvent>` interfaces**
- [ ] **Step 8: Update NotificationAgent to use `IStreamConsumer<T>` instead of `HandleEvent` override**
- [ ] **Step 9: Update MCP AgentTools, DevUI, and any other callers of HandleEvent**
- [ ] **Step 10: Run all tests — update tests that used HandleEvent/PublishAsync**

Run: `dotnet test IAW.slnx -v n`
Expected: PASS

- [ ] **Step 11: Commit**

```bash
git add src/Core/Agent/Agent.Events.cs src/Core/Agent/Agent.Streams.cs src/Core/Contracts/IAgent.cs src/Agents/ src/IAW.MCP/ test/Core.Tests/
git commit -m "feat: typed event publishing, remove HandleEvent and PublishAsync from IAgent"
```

---

### Task 6: Auto-logging across all channels

**Files:**
- Modify: `src/Core/Agent/Agent.Events.cs` — already done in Task 5 for streams
- Modify: `src/Core/Agent/Agent.cs` — auto-log GetResponse calls (conversation logic is in the main Agent.cs partial, not a separate Conversation file)
- Modify: `src/Core/Communication/IReceiver.cs` — add logging hook
- Test: `test/Core.Tests/Communication/AutoLoggingTests.cs`

- [ ] **Step 1: Write tests for auto-logging**

```csharp
public class AutoLoggingTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetResponse_auto_logs_LlmCall()
    {
        var agent = Agent("test-autolog");
        await agent.GetResponse("hello", default);
        var log = await agent.GetEventLog(default);
        Assert.Contains(log, e => e.EventName == "LlmCall");
    }

    [Fact]
    public async Task IReceiver_Receive_auto_logs()
    {
        // Test that receiving a P2P message auto-logs
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Add auto-logging to Agent.Conversation.cs after LLM call**
- [ ] **Step 4: Add auto-logging wrapper around IReceiver.Receive in Agent base**
- [ ] **Step 5: Run tests — verify pass**
- [ ] **Step 6: Commit**

```bash
git add src/Core/Agent/ test/Core.Tests/Communication/AutoLoggingTests.cs
git commit -m "feat: auto-log all communication channels to agent event log"
```

---

### Task 7: Rename INotification to INotificationAgent

**Files:**
- Modify: `src/Agents/Orchestration/INotification.cs` — rename interface
- Modify: `src/Agents/Orchestration/NotificationAgent.cs` — update implementation
- Modify: `src/Agents/Orchestration/PersonalAssistantAgent.cs` — update reference
- Modify: `src/IAW.MCP/Tools/AgentTools.cs` — update grain resolution
- Test: existing tests must pass

- [ ] **Step 1: Rename INotification to INotificationAgent in interface file**
- [ ] **Step 2: Update NotificationAgent to implement INotificationAgent**
- [ ] **Step 3: Update all references (PersonalAssistant, MCP tools)**
- [ ] **Step 4: Run all tests**
- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/ src/IAW.MCP/
git commit -m "refactor: rename INotification to INotificationAgent to avoid collision"
```

---

## Chunk 2: InterfaceCatalog — Interface-Only Discovery

### Task 8: Port InterfaceCatalog from local IAW

**Files:**
- Create: `src/Core/Orchestration/InterfaceCatalog.cs` (or update existing)
- Test: `test/Core.Tests/Orchestration/InterfaceCatalogTests.cs` (or update existing)

- [ ] **Step 1: Write/update tests for grain ID computation**

```csharp
public class InterfaceCatalogTests
{
    [Theory]
    [InlineData(typeof(IRoslyn), "roslyn")]
    [InlineData(typeof(IFileSystem), "file-system")]
    [InlineData(typeof(IPersonalAssistant), "personal-assistant")]
    [InlineData(typeof(IDotNet), "dot-net")]
    [InlineData(typeof(INuGet), "nu-get")]
    public void ComputeGrainId_converts_interface_to_kebab_case(Type interfaceType, string expected)
    {
        var grainId = InterfaceCatalog.ComputeGrainId(interfaceType);
        Assert.Equal(expected, grainId);
    }

    [Fact]
    public void Discover_finds_all_agent_interfaces()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IRoslyn");
        Assert.Contains(catalog, e => e.InterfaceName == "IFileSystem");
    }

    [Fact]
    public void Discover_excludes_base_interfaces()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.DoesNotContain(catalog, e => e.InterfaceName == "IAgent");
        Assert.DoesNotContain(catalog, e => e.InterfaceName == "IDynamicAgent");
    }

    [Fact]
    public void Discover_detects_stream_producers_and_consumers()
    {
        var catalog = InterfaceCatalog.Discover();
        // Agents implementing IStreamProducer<T> should have Produces entries
        // Agents implementing IStreamConsumer<T> should have Consumes entries
    }

    [Fact]
    public void ToPromptString_generates_LLM_readable_catalog()
    {
        var catalog = InterfaceCatalog.Discover();
        var prompt = InterfaceCatalog.ToPromptString(catalog);
        Assert.Contains("IRoslyn", prompt);
        Assert.Contains("GetResponse", prompt);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement InterfaceCatalog**

Port from `E:\IAW\src\Core\Orchestration\InterfaceCatalog.cs`. Key methods:
- `static IReadOnlyList<CatalogEntry> Discover()` — scans AppDomain
- `static string ComputeGrainId(Type interfaceType)` — kebab-case conversion
- `static string ToPromptString(IReadOnlyList<CatalogEntry> entries)` — LLM format

- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Update AgentRegistrationStartupTask to use InterfaceCatalog**

Replace attribute-based scanning with InterfaceCatalog for subscriber pre-activation.

- [ ] **Step 6: Run all tests**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 7: Commit**

```bash
git add src/Core/Orchestration/InterfaceCatalog.cs src/Core/Registry/AgentRegistrationStartupTask.cs test/Core.Tests/Orchestration/InterfaceCatalogTests.cs
git commit -m "feat: add InterfaceCatalog for interface-only agent discovery"
```

---

### Task 9: Update MCP tools to use InterfaceCatalog

**Files:**
- Modify: `src/IAW.MCP/Tools/AgentTools.cs`
- Test: manual verification via `aspire run`

- [ ] **Step 1: Replace hardcoded agent list in AgentTools with InterfaceCatalog.Discover()**
- [ ] **Step 2: Build and verify**

Run: `dotnet build IAW.slnx`

- [ ] **Step 3: Commit**

```bash
git add src/IAW.MCP/Tools/AgentTools.cs
git commit -m "feat: MCP agent_list_all uses InterfaceCatalog"
```

---

## Chunk 3: LLM as Agent

### Task 10: Create LLM abstract base class

**Files:**
- Create: `src/Core/LLM.cs`
- Test: `test/Core.Tests/LLMAgentTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class LLMAgentTests
{
    [Fact]
    public void LLM_extends_Agent()
    {
        Assert.True(typeof(LLM).IsSubclassOf(typeof(Agent)));
    }

    [Fact]
    public void LLM_is_abstract()
    {
        Assert.True(typeof(LLM).IsAbstract);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement LLM base class**

```csharp
namespace IAW.Core;

public abstract class LLM(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient)
    : Agent(state, eventLog, chatClient)
{
    protected override string Instructions =>
        $"You are {DisplayName}. Answer directly and accurately.";
}
```

- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/LLM.cs test/Core.Tests/LLMAgentTests.cs
git commit -m "feat: add LLM abstract base class"
```

---

### Task 11: Add new LLMModel definitions

**Important:** LLMModel classes (in `src/Core/AI/Models/`) define model metadata (ID, provider, capabilities). LLM agent interfaces (like `IOpus46`) are grain interfaces that go in `src/Agents/LLM/` alongside their agent implementations (Task 12), NOT in `src/Core/AI/Models/`. The existing pattern puts interfaces next to implementations (e.g., `IRoslyn.cs` is in `src/Agents.CSharp/`).

**Files:**
- Create: `src/Core/AI/Models/Opus46Model.cs` (model metadata — named `Opus46Model` to avoid collision with agent class `Opus46`)
- Create: `src/Core/AI/Models/Gpt52Model.cs`
- Create: `src/Core/AI/Models/Gpt53Model.cs`
- Create: `src/Core/AI/Models/Gemini31Model.cs`
- Create: `src/Core/AI/Models/GrokLatestModel.cs`
- Modify: `src/Core/AI/LLMModel.cs` — update EnsureAllModelsLoaded
- Test: `test/Core.Tests/Models/LLMModelTests.cs`

**Note on naming:** The existing models already use the model name directly as the class name (e.g., `Sonnet46 : LLMModel`). To avoid collision with agent grain classes that the user wants named `Opus46 : LLM`, we suffix model metadata classes with `Model`: `Opus46Model : LLMModel`. The `[Llm<Opus46Model>]` attribute then references the metadata class. Update existing model classes (`Sonnet46` -> `Sonnet46Model`, `Claude45Haiku` -> `Claude45HaikuModel`, etc.) for consistency, or keep the existing names and only suffix new ones. Decide based on codebase convention during implementation.

- [ ] **Step 1: Write tests**

```csharp
public class LLMModelTests
{
    [Fact]
    public void EnsureAllModelsLoaded_includes_all_new_models()
    {
        LLMModel.EnsureAllModelsLoaded();
        Assert.Contains(LLMModel.All, m => m is Opus46);
        Assert.Contains(LLMModel.All, m => m is Gpt52);
        Assert.Contains(LLMModel.All, m => m is Gpt53);
        Assert.Contains(LLMModel.All, m => m is Gemini31);
        Assert.Contains(LLMModel.All, m => m is GrokLatest);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement model singletons following existing pattern (e.g., Claude45Haiku.cs)**

Each model: class + interface. Follow existing pattern:
```csharp
public sealed class Opus46 : LLMModel
{
    public static readonly Opus46 Instance = new();
    private Opus46() { }
    public override string Id => "claude-opus-4-6";
    public override string DisplayName => "Claude Opus 4.6";
    public override ProviderType Provider => ProviderType.Anthropic;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IOpus46 : IAgent { }
```

- [ ] **Step 4: Update EnsureAllModelsLoaded**
- [ ] **Step 5: Run tests — verify pass**
- [ ] **Step 6: Commit**

```bash
git add src/Core/AI/Models/ src/Core/AI/LLMModel.cs test/Core.Tests/Models/LLMModelTests.cs
git commit -m "feat: add Opus46, Gpt52, Gpt53, Gemini31, GrokLatest model definitions"
```

---

### Task 12: Create concrete LLM agent classes

**Files:**
- Create: `src/Agents/LLM/Opus46Agent.cs` (class name `Opus46` but file named for clarity)
- Create: `src/Agents/LLM/Gpt52Agent.cs`
- Create: one file per model (11 total)
- Modify: `src/Agents/Agents.csproj` — ensure new files included
- Test: `test/Core.Tests/LLMAgentInstanceTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class LLMAgentInstanceTests : AgentTest<Opus46>
{
    [Fact]
    public async Task Opus46_responds_via_GetResponse()
    {
        var agent = Agent("opus46");
        var response = await agent.GetResponse("hello", default);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Opus46_metadata_shows_correct_display_name()
    {
        var agent = Agent("opus46");
        var metadata = await agent.GetMetadata(default);
        Assert.Equal("Claude Opus 4.6", metadata.DisplayName);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement LLM agent classes**

Each one follows this pattern:
```csharp
namespace IAW.Agents.LLM;

public class Opus46(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Core.AI.Models.Opus46>] IChatClient chatClient)
    : LLM(state, eventLog, chatClient), IOpus46
{
    protected override string DisplayName => Core.AI.Models.Opus46.Instance.DisplayName;
}
```

- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Agents/LLM/ test/Core.Tests/LLMAgentInstanceTests.cs
git commit -m "feat: add 11 LLM agent implementations"
```

---

## Chunk 4: Memory Agents

### Task 13: Add NuGet packages for embeddings and vector search

**Files:**
- Modify: `Directory.Packages.props` — add ElBruno.LocalEmbeddings, Microsoft.Extensions.VectorData

- [ ] **Step 1: Add package versions to Directory.Packages.props**

```xml
<PackageVersion Include="ElBruno.LocalEmbeddings" Version="1.1.4" />
<PackageVersion Include="ElBruno.LocalEmbeddings.VectorData" Version="*" />
<PackageVersion Include="Microsoft.Extensions.VectorData.Abstractions" Version="*" />
```

Note: Qdrant packages already exist in Directory.Packages.props.

- [ ] **Step 2: Build to verify packages resolve**

Run: `dotnet restore IAW.slnx && dotnet build IAW.slnx`

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore: add ElBruno.LocalEmbeddings and VectorData packages"
```

---

### Task 14: Create Memory abstract base class

**Files:**
- Create: `src/Core/Memory.cs`
- Modify: `src/Core/Core.csproj` — add VectorData/Embedding package references
- Test: `test/Core.Tests/MemoryBaseTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class MemoryBaseTests
{
    [Fact]
    public void Memory_extends_Agent()
    {
        Assert.True(typeof(Memory).IsSubclassOf(typeof(Agent)));
    }

    [Fact]
    public void Memory_is_abstract()
    {
        Assert.True(typeof(Memory).IsAbstract);
    }

    [Fact]
    public void Memory_has_Observe_method()
    {
        var method = typeof(Memory).GetMethod("Observe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void Memory_has_Search_method()
    {
        var method = typeof(Memory).GetMethod("Search", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement Memory base class**

```csharp
namespace IAW.Core;

public abstract class Memory(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IVectorStore vectorStore)
    : Agent(state, eventLog, chatClient), IStreamConsumer<ITaskStreamEvent>
{
    protected abstract string CollectionName { get; }

    public async Task Observe(string content, MemoryProvenance provenance)
    {
        var embedding = await embedder.GenerateVectorAsync(content);
        var entry = new MemoryEntry(
            Guid.NewGuid().ToString("N"),
            content, provenance, 1.0f,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null);
        await memories.AddAsync(entry);
        // store in vector store collection
    }

    public async Task<IReadOnlyList<MemoryEntry>> Search(string query, int topK = 5, float minScore = 0.3f)
    {
        var queryVector = await embedder.GenerateVectorAsync(query);
        // search vector store, return top matches
        return [];
    }

    public async Task Consolidate() { /* merge similar, LLM reasoning */ }
    public async Task Decay() { /* reduce relevance over time */ }
    public async Task Forget(string memoryId) { /* remove */ }

    public Task OnStreamEventAsync(ITaskStreamEvent evt, StreamSequenceToken? token)
    {
        // observe task stream events
        return Observe(evt.ToString()!, new MemoryProvenance(
            "task-stream", evt.TaskId, evt.AgentId, evt.GetType().Name,
            evt.Timestamp, null, 0.7f));
    }
}
```

- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Memory.cs src/Core/Core.csproj test/Core.Tests/MemoryBaseTests.cs
git commit -m "feat: add Memory abstract base class with embedding and vector search"
```

---

### Task 15: Create concrete Memory agent classes

**Files:**
- Create: `src/Agents/Memory/UserMemory.cs` + `IUserMemory.cs`
- Create: `src/Agents/Memory/ProjectMemory.cs` + `IProjectMemory.cs`
- Create: `src/Agents/Memory/PatternMemory.cs` + `IPatternMemory.cs`
- Create: `src/Agents/Memory/EpisodeMemory.cs` + `IEpisodeMemory.cs`
- Create: `src/Agents/Memory/CodeMemory.cs` + `ICodeMemory.cs`
- Test: `test/Core.Tests/MemoryAgentTests.cs`

- [ ] **Step 1: Write tests for each memory agent**

```csharp
public class UserMemoryTests : AgentTest<UserMemory>
{
    [Fact]
    public async Task UserMemory_can_observe_and_search()
    {
        var agent = Agent("user-memory");
        // test observe + search round trip
    }

    [Fact]
    public async Task UserMemory_metadata_correct()
    {
        var agent = Agent("user-memory");
        var meta = await agent.GetMetadata(default);
        Assert.Equal("User Memory", meta.DisplayName);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement all 5 memory agents**

Each follows pattern:
```csharp
namespace IAW.Agents.Memory;

public class UserMemory(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    IVectorStore vectorStore)
    : Memory(state, eventLog, memories, chatClient, embedder, vectorStore), IUserMemory
{
    protected override string CollectionName => "iaw-user-memory";
    protected override string DisplayName => "User Memory";
    protected override string Instructions =>
        "You manage user preferences, personal facts, and corrections. Extract and remember personal information from conversations.";
}
```

- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Agents/Memory/ test/Core.Tests/MemoryAgentTests.cs
git commit -m "feat: add 5 specialized Memory agents"
```

---

## Chunk 5: Context Providers

### Task 16: Implement MemoryContextProvider

**Files:**
- Create: `src/Core/Context/MemoryContextProvider.cs`
- Test: `test/Core.Tests/Context/MemoryContextProviderTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class MemoryContextProviderTests
{
    [Fact]
    public async Task ProvideContextAsync_queries_memory_agents()
    {
        // mock memory agents, verify provider queries them
        // verify results ranked by relevance * trust
    }

    [Fact]
    public async Task ProvideContextAsync_returns_empty_when_no_memories()
    {
        var provider = new MemoryContextProvider(/* empty memory agents */);
        var context = await provider.ProvideContextAsync([], default);
        Assert.Equal(0, context.AdditionalMessages.Count);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement MemoryContextProvider**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Context/MemoryContextProvider.cs test/Core.Tests/Context/MemoryContextProviderTests.cs
git commit -m "feat: add MemoryContextProvider for automatic memory injection"
```

---

### Task 17: Implement TaskStreamContextProvider

**Files:**
- Create: `src/Core/Context/TaskStreamContextProvider.cs`
- Test: `test/Core.Tests/Context/TaskStreamContextProviderTests.cs`

- [ ] **Step 1: Write tests**
- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement TaskStreamContextProvider**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Context/TaskStreamContextProvider.cs test/Core.Tests/Context/TaskStreamContextProviderTests.cs
git commit -m "feat: add TaskStreamContextProvider for task stream context injection"
```

---

### Task 18: Wire context providers into Agent base class

**Files:**
- Modify: `src/Core/Agent/Agent.cs` — override GetContextProviders with defaults
- Test: verify existing tests still pass + new integration test

- [ ] **Step 1: Add default context providers to Agent base**

```csharp
protected override IReadOnlyList<IAIContextProvider> GetContextProviders() =>
[
    // only active when memory agents and task context are available
    ..(_memoryContextProvider is not null ? [_memoryContextProvider] : []),
    ..(_taskStreamContextProvider is not null ? [_taskStreamContextProvider] : []),
];
```

- [ ] **Step 2: Run all tests**
- [ ] **Step 3: Commit**

```bash
git add src/Core/Agent/Agent.cs
git commit -m "feat: wire MemoryContextProvider and TaskStreamContextProvider into Agent base"
```

---

## Chunk 6: Code Orchestration

### Task 19: Port OrchestrationCompiler

**Files:**
- Create or update: `src/Agents.CSharp/OrchestrationCompiler.cs`
- Test: `test/Core.Tests/Orchestration/OrchestrationCompilerTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class OrchestrationCompilerTests
{
    [Fact]
    public void Compile_valid_source_succeeds()
    {
        var source = """
            using System;
            Console.WriteLine("hello");
            """;
        var result = OrchestrationCompiler.Compile(source);
        Assert.True(result.Success);
    }

    [Fact]
    public void Compile_invalid_source_returns_errors()
    {
        var source = "int x = \"not a number\";";
        var result = OrchestrationCompiler.Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Compile_with_agent_interfaces_resolves_types()
    {
        var source = """
            var roslyn = client.GetGrain<IRoslyn>("roslyn");
            """;
        // should compile when agent interface assemblies are referenced
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Port OrchestrationCompiler from E:\IAW\src\Agents\CSharp\OrchestrationCompiler.cs**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/OrchestrationCompiler.cs test/Core.Tests/Orchestration/OrchestrationCompilerTests.cs
git commit -m "feat: add OrchestrationCompiler for Roslyn-based script validation"
```

---

### Task 20: Update ScriptGenerator for typed interfaces

**Files:**
- Modify: `src/Core/Orchestration/ScriptGenerator.cs`
- Test: `test/Core.Tests/Orchestration/ScriptGeneratorTests.cs` (update existing)

- [ ] **Step 1: Update tests to expect typed grain references**

```csharp
[Fact]
public void Generate_uses_typed_interfaces()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "analyze code" })
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000, "/workspace");
    Assert.Contains("GetGrain<IRoslyn>", script);
    Assert.DoesNotContain("GetGrain<IAgent>", script);
}

[Fact]
public void Generate_uses_GetResponse_not_SendMessageAsync()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000, "/workspace");
    Assert.Contains("GetResponse", script);
    Assert.DoesNotContain("SendMessageAsync", script);
}
```

- [ ] **Step 2: Run tests — verify fail (still generating old format)**
- [ ] **Step 3: Update ScriptGenerator to use InterfaceCatalog for type resolution**
- [ ] **Step 4: Fix method names (GetResponse not SendMessageAsync, SetWorkspace not SetWorkspaceAsync)**
- [ ] **Step 5: Run tests — verify pass**
- [ ] **Step 6: Commit**

```bash
git add src/Core/Orchestration/ScriptGenerator.cs test/Core.Tests/Orchestration/ScriptGeneratorTests.cs
git commit -m "feat: ScriptGenerator uses typed interfaces and correct method names"
```

---

### Task 21: Update ScriptExecutor to validate before execution

**Files:**
- Modify: `src/Core/Orchestration/ScriptExecutor.cs`
- Test: `test/Core.Tests/Orchestration/ScriptExecutorTests.cs`

- [ ] **Step 1: Update tests**

```csharp
[Fact]
public async Task ExecuteScriptAsync_validates_before_running()
{
    var invalidSource = "this is not valid C#;;;";
    var result = await ScriptExecutor.ExecuteScriptAsync(invalidSource, "/tmp");
    Assert.False(result.Success);
    Assert.Contains("compilation", result.Error, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Add OrchestrationCompiler.Compile() call before dotnet run in ScriptExecutor**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Core/Orchestration/ScriptExecutor.cs test/Core.Tests/Orchestration/ScriptExecutorTests.cs
git commit -m "feat: ScriptExecutor validates compilation before execution"
```

---

### Task 22: Implement CodeOrchestrator agent

**Files:**
- Create: `src/Agents/Orchestration/CodeOrchestrator.cs`
- Create: `src/Agents/Orchestration/ICodeOrchestrator.cs`
- Test: `test/Core.Tests/CodeOrchestratorTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class CodeOrchestratorTests : AgentTest<CodeOrchestrator>
{
    [Fact]
    public async Task CreateTask_stores_plan_in_durable_state()
    {
        var agent = Agent("code-orchestrator");
        var taskId = await ((ICodeOrchestrator)agent).CreateTask("Fix build errors", default);
        Assert.NotNull(taskId);
    }

    [Fact]
    public async Task GetTaskState_returns_current_status()
    {
        var agent = Agent("code-orchestrator");
        var orch = (ICodeOrchestrator)agent;
        var taskId = await orch.CreateTask("Test task", default);
        var state = await orch.GetTaskState(taskId, default);
        Assert.Equal(OrchestrationStatus.Created, state.Status);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement CodeOrchestrator with durable state per spec Section 4**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestrator.cs src/Agents/Orchestration/ICodeOrchestrator.cs test/Core.Tests/CodeOrchestratorTests.cs
git commit -m "feat: add CodeOrchestrator with durable step tracking and recovery"
```

---

## Chunk 7: TaskSupervisor and Notification

### Task 23: Implement TaskSupervisor agent

**Files:**
- Create: `src/Agents/Orchestration/TaskSupervisor.cs`
- Create: `src/Agents/Orchestration/ITaskSupervisor.cs`
- Create: `src/Core/Models/TaskHealthRecord.cs`
- Test: `test/Core.Tests/TaskSupervisorTests.cs`

- [ ] **Step 1: Write tests**

```csharp
public class TaskSupervisorTests : AgentTest<TaskSupervisor>
{
    [Fact]
    public async Task Supervisor_tracks_active_tasks()
    {
        var agent = Agent("task-supervisor");
        var meta = await agent.GetMetadata(default);
        Assert.Equal("Task Supervisor", meta.DisplayName);
    }
}
```

- [ ] **Step 2: Run tests — verify fail**
- [ ] **Step 3: Implement TaskSupervisor per spec Section 7**
- [ ] **Step 4: Run tests — verify pass**
- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/TaskSupervisor.cs src/Agents/Orchestration/ITaskSupervisor.cs src/Core/Models/TaskHealthRecord.cs test/Core.Tests/TaskSupervisorTests.cs
git commit -m "feat: add TaskSupervisor for task health monitoring"
```

---

### Task 24: Update Notification agent with channel routing

**Files:**
- Modify: `src/Agents/Orchestration/NotificationAgent.cs`
- Modify: `src/Agents/Orchestration/INotification.cs` (already renamed to INotificationAgent in Task 7)
- Test: existing notification tests + new channel routing tests

- [ ] **Step 1: Add NotificationRequest and channel routing logic**
- [ ] **Step 2: Run tests — verify pass**
- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/NotificationAgent.cs
git commit -m "feat: notification agent with channel-aware routing"
```

---

## Chunk 8: Persistence — CosmosDB + Qdrant Aspire Integration

### Task 25: Add CosmosDB and persistence packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/IAW.AppHost/Aspire.csproj`

- [ ] **Step 1: Add packages to Directory.Packages.props**

**Important:** Use versions compatible with Orleans 10.0. Check NuGet for the latest 10.x compatible versions. Do NOT use 9.x packages with Orleans 10.0 — they will cause assembly binding failures.

```xml
<PackageVersion Include="Microsoft.Orleans.Persistence.Cosmos" Version="10.0.1" />
<PackageVersion Include="Microsoft.Orleans.Clustering.Cosmos" Version="10.0.1" />
<PackageVersion Include="Microsoft.Orleans.Reminders.Cosmos" Version="10.0.1" />
<PackageVersion Include="Aspire.Hosting.Azure.CosmosDB" Version="13.1.2" />
<PackageVersion Include="Aspire.Microsoft.Azure.Cosmos" Version="13.1.2" />
```

- [ ] **Step 2: Add package references to AppHost csproj**
- [ ] **Step 3: Build to verify**

Run: `dotnet restore IAW.slnx && dotnet build IAW.slnx`

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props src/IAW.AppHost/Aspire.csproj
git commit -m "chore: add CosmosDB and Orleans persistence packages"
```

---

### Task 26: Implement WithCosmosStorage and WithQdrant extensions

**Files:**
- Modify: `src/IAW.AppHost/IAWExtensions.cs` (or equivalent hosting extensions file)
- Test: build verification

- [ ] **Step 1: Add WithCosmosStorage extension**

```csharp
public static OrleansService WithCosmosStorage(this OrleansService orleans, IResourceBuilder<AzureCosmosDBResource> cosmos)
{
    // swap memory grain storage for CosmosDB
    // swap memory reminders for CosmosDB
    // swap localhost clustering for CosmosDB
    return orleans;
}

public static OrleansService WithQdrant(this OrleansService orleans, IResourceBuilder<QdrantServerResource> qdrant)
{
    // register QdrantClient in DI
    return orleans;
}

public static OrleansService WithLocalEmbeddings(this OrleansService orleans)
{
    // register ElBruno.LocalEmbeddings IEmbeddingGenerator
    return orleans;
}
```

- [ ] **Step 2: Update AppHost.cs with opt-in example (commented out by default)**
- [ ] **Step 3: Build and verify**
- [ ] **Step 4: Commit**

```bash
git add src/IAW.AppHost/
git commit -m "feat: add WithCosmosStorage, WithQdrant, WithLocalEmbeddings extensions"
```

---

## Chunk 9: Architecture Guard Tests

### Task 27: Add architecture guard tests for v0.2.0 invariants

**Files:**
- Modify: `test/Core.Tests/` (existing architecture guard tests file)
- Create: `test/Core.Tests/ArchitectureGuardV2Tests.cs`

- [ ] **Step 1: Write architecture guards**

```csharp
public class ArchitectureGuardV2Tests
{
    [Fact]
    public void No_public_AgentEvent_construction_in_agent_code()
    {
        // scan agent assemblies for new AgentEvent() calls
        // only Core internals should construct AgentEvent
    }

    [Fact]
    public void All_agents_have_matching_interfaces()
    {
        var agentTypes = typeof(Agent).Assembly.GetTypes()
            .Concat(typeof(PersonalAssistantAgent).Assembly.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(Agent)) && !t.IsAbstract);
        foreach (var agent in agentTypes)
        {
            var iface = agent.GetInterfaces()
                .FirstOrDefault(i => i.Name == $"I{agent.Name}" || i.Name == $"I{agent.Name.Replace("Agent", "")}");
            Assert.NotNull(iface);
        }
    }

    [Fact]
    public void All_stream_event_types_implement_IEvent()
    {
        var eventTypes = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Event") && t.IsClass && !t.IsAbstract);
        Assert.All(eventTypes, t => Assert.True(t.IsAssignableTo(typeof(IEvent))));
    }

    [Fact]
    public void Memory_entries_always_have_provenance_fields()
    {
        var props = typeof(MemoryEntry).GetProperties();
        Assert.Contains(props, p => p.Name == "Source" && p.PropertyType == typeof(MemoryProvenance));
    }

    [Fact]
    public void LLM_agents_extend_LLM_base()
    {
        // LLM agents live in the Agents assembly, not Core
        var agentsAssembly = typeof(PersonalAssistantAgent).Assembly;
        var llmAgents = agentsAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(LLM)) && !t.IsAbstract);
        Assert.NotEmpty(llmAgents);
    }

    [Fact]
    public void Memory_agents_extend_Memory_base()
    {
        // Memory agents live in the Agents assembly, not Core
        var agentsAssembly = typeof(PersonalAssistantAgent).Assembly;
        var memoryAgents = agentsAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Memory)) && !t.IsAbstract);
        Assert.NotEmpty(memoryAgents);
    }
}
```

- [ ] **Step 2: Run tests — verify pass (all invariants should hold after implementation)**
- [ ] **Step 3: Commit**

```bash
git add test/Core.Tests/ArchitectureGuardV2Tests.cs
git commit -m "test: add v0.2.0 architecture guard tests"
```

---

## Chunk 10: Website Documentation

### Task 28: Write new guide pages

**Files:**
- Create: `website/guide/communication.md`
- Create: `website/guide/orchestration.md`
- Create: `website/guide/consilium.md`
- Create: `website/guide/memory.md`
- Create: `website/guide/llm-agents.md`
- Create: `website/guide/persistence.md`
- Create: `website/guide/supervisor.md`

- [ ] **Step 1: Write communication.md**

Cover three channels (task streams, typed pub/sub, P2P), when to use which, combined flow example from spec.

- [ ] **Step 2: Write orchestration.md**

Cover CodeOrchestrator, InterfaceCatalog, typed scripts, all 7 scenarios from spec with runnable code.

- [ ] **Step 3: Write consilium.md**

Cover multi-model patterns: adaptive routing, majority vote, synthesis. Code examples.

- [ ] **Step 4: Write memory.md**

Cover Memory agents, provenance, trust scores, consolidation, decay, auto-injection.

- [ ] **Step 5: Write llm-agents.md**

Cover LLM hierarchy, how to add new models, grain pooling for parallelism.

- [ ] **Step 6: Write persistence.md**

Cover CosmosDB emulator setup, Qdrant setup, in-memory vs durable mode, AppHost configuration.

- [ ] **Step 7: Write supervisor.md**

Cover TaskSupervisor, health monitoring, stall detection, escalation.

- [ ] **Step 8: Commit**

```bash
git add website/guide/
git commit -m "docs: add v0.2.0 guide pages for communication, orchestration, memory, LLM, persistence"
```

---

### Task 29: Update existing guide pages

**Files:**
- Modify: `website/guide/events-streams.md` — rewrite for typed-only events
- Modify: `website/guide/agents.md` — updated hierarchy (Agent, LLM, Memory)
- Modify: `website/guide/testing.md` — new test patterns
- Modify: `website/guide/architecture.md` — v0.2.0 architecture diagram

- [ ] **Step 1: Rewrite events-streams.md**
- [ ] **Step 2: Update agents.md with new hierarchy**
- [ ] **Step 3: Update testing.md with memory/orchestration test patterns**
- [ ] **Step 4: Update architecture.md with full v0.2.0 diagram**
- [ ] **Step 5: Commit**

```bash
git add website/guide/
git commit -m "docs: update existing guides for v0.2.0 changes"
```

---

### Task 30: Update CONTRIBUTING.md and CHANGELOG.md

**Files:**
- Modify: `CONTRIBUTING.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add to CONTRIBUTING.md**

- How to add a new LLM model (create LLMModel + interface + LLM subclass + update EnsureAllModelsLoaded)
- How to add a new Memory type (extend Memory, set CollectionName, define specialization)
- How to write orchestration scenarios
- Testing requirements for new agents

- [ ] **Step 2: Add v0.2.0 to CHANGELOG.md**

Document all new features, breaking changes per spec Section 12.

- [ ] **Step 3: Commit**

```bash
git add CONTRIBUTING.md CHANGELOG.md
git commit -m "docs: update CONTRIBUTING.md and CHANGELOG.md for v0.2.0"
```

---

## Chunk 11: Integration Tests

### Task 31: End-to-end orchestration integration test

**Files:**
- Create: `test/Integration.Tests/OrchestrationIntegrationTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
public class OrchestrationIntegrationTests : AspireAgentTest<Agent>
{
    [Fact]
    public async Task CodeOrchestrator_creates_and_tracks_task()
    {
        var orchestrator = Client.GetGrain<ICodeOrchestrator>("code-orchestrator");
        var taskId = await orchestrator.CreateTask("Analyze src/Core/Agent.cs", default);
        Assert.NotNull(taskId);

        var state = await orchestrator.GetTaskState(taskId, default);
        Assert.Equal(OrchestrationStatus.Created, state.Status);
    }
}
```

- [ ] **Step 2: Run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj -v n`

- [ ] **Step 3: Commit**

```bash
git add test/Integration.Tests/OrchestrationIntegrationTests.cs
git commit -m "test: add orchestration integration tests"
```

---

### Task 32: LLM agent integration test

**Files:**
- Create: `test/Integration.Tests/LLMAgentIntegrationTests.cs`

- [ ] **Step 1: Write test verifying LLM agents are discoverable and respond**

```csharp
public class LLMAgentIntegrationTests : AspireAgentTest<Agent>
{
    [Fact]
    public async Task LLM_agents_appear_in_InterfaceCatalog()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IOpus46");
        Assert.Contains(catalog, e => e.InterfaceName == "ISonnet46");
    }
}
```

- [ ] **Step 2: Run integration tests**
- [ ] **Step 3: Commit**

```bash
git add test/Integration.Tests/LLMAgentIntegrationTests.cs
git commit -m "test: add LLM agent integration tests"
```

---

### Task 33: Memory agent integration test

**Files:**
- Create: `test/Integration.Tests/MemoryIntegrationTests.cs`

- [ ] **Step 1: Write test for memory observe/search cycle**
- [ ] **Step 2: Run integration tests**
- [ ] **Step 3: Commit**

```bash
git add test/Integration.Tests/MemoryIntegrationTests.cs
git commit -m "test: add memory agent integration tests"
```

---

### Task 34: Final full test run and build verification

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test IAW.slnx -v n`
Expected: ALL PASS

- [ ] **Step 2: Run all integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj -v n`
Expected: ALL PASS

- [ ] **Step 3: Build solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 warnings (or only pre-existing warnings)

- [ ] **Step 4: Run aspire**

Run: `aspire run --project src/IAW.AppHost/Aspire.csproj`
Verify: Dashboard loads, agents register, MCP tools respond

- [ ] **Step 5: Tag release**

```bash
git tag v0.2.0
```

---

## Task Dependency Graph

```
Tasks 1-3 (type defs, parallel) ─── Task 4 (Agent constructor) ─── Task 5 (typed publish) ─── Task 6 (auto-log) ─── Task 7 (rename INotification)
                                                                         │
                                                                         ├── Task 8 (InterfaceCatalog) ─── Task 9 (MCP update)
                                                                         │                                      │
                                                                         ├── Task 10 (LLM base) ── Task 11 (LLM models) ── Task 12 (LLM agents)
                                                                         │
                                                                         ├── Task 13 (packages) ── Task 14 (Memory base) ── Task 15 (Memory agents)
                                                                         │                                                       │
                                                                         │                          Task 16 (MemoryCtxProvider) ──┤
                                                                         │                          Task 17 (TaskStreamCtxProv) ──┼── Task 18 (wire into Agent)
                                                                         │
                                                                         ├── Task 19 (OrchCompiler)─┐
                                                                         │   Task 8 ────────────────┼── Task 20 (ScriptGen) ── Task 21 (ScriptExec) ── Task 22 (CodeOrchestrator)
                                                                         │
                                                                         ├── Task 23 (TaskSupervisor) ── Task 24 (Notification)
                                                                         │
                                                                         └── Task 25 (packages) ── Task 26 (Aspire extensions)

Task 27 (architecture guards) ─── depends on Tasks 12, 15 (agents must exist)
Tasks 28-30 (docs) ─── can run in parallel with implementation
Tasks 31-33 (integration tests) ─── depend on Tasks 12, 15, 22
Task 34 (final verification) ─── depends on ALL above
```

**Key cross-dependencies:**
- Task 8 (InterfaceCatalog) depends on Task 7 (INotification rename must be in place)
- Task 16 (MemoryContextProvider) depends on Task 15 (Memory agent interfaces must exist)
- Task 18 (wire providers) depends on Tasks 15, 16, 17
- Task 20 (ScriptGenerator update) depends on Task 8 (InterfaceCatalog for type resolution)
- Task 22 (CodeOrchestrator) depends on Tasks 8 and 21

**Parallelizable groups after Task 5:**
- Tasks 8, 10, 13, 19, 25 can start in parallel
- Tasks 28-30 (docs) can run anytime in parallel
- Tasks 31-33 can run in parallel after their respective implementation tasks complete
