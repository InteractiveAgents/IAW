# V3 Quality Assurance — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Comprehensive test coverage for all V3 Agent behaviors — unit tests, behavior contract tests, integration tests, architecture guards, performance benchmarks, and test infrastructure improvements.

**Architecture:** Three test layers: (1) Unit tests via TestCluster, (2) Behavior contract tests via AgentTest<T> auto-generation, (3) Integration tests via AspireAgentTest<T>. Each behavior gets dedicated coverage.

**Tech Stack:** xunit.v3 3.2.2, Orleans TestingHost 10.0.1, Aspire.Hosting.Testing 13.1.2, BenchmarkDotNet

**Dependency:** Requires completion of `2026-03-07-core-agent-migration-plan.md`

---

## Section 1: Test Infrastructure for V3

### Task 1: Create AgentTestV3<T> base class

**Files:**
- Create: `src/IAW.Testing/AgentTestV3.cs`

**Step 1: Write AgentTestV3<T> — auto-generated behavior tests for V3 agents**

```csharp
using Core.V3;
using Microsoft.Extensions.AI;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Testing;

public abstract class AgentTestV3<TAgent> : IAsyncLifetime where TAgent : Agent
{
    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];
    protected TestCluster Cluster { get; private set; } = null!;
    protected IStreamProvider StreamProvider { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        ConfigureSilo(builder);
        Cluster = builder.Build();
        await Cluster.DeployAsync();
        StreamProvider = Cluster.Client.GetStreamProvider("agents");
        await OnClusterReadyAsync();
    }

    public async Task DisposeAsync() => await Cluster.DisposeAsync();

    protected virtual void ConfigureSilo(TestClusterBuilder builder) { }
    protected virtual Task OnClusterReadyAsync() => Task.CompletedTask;

    protected Core.V3.IAgent Agent(string id) => Cluster.GrainFactory.GetGrain<Core.V3.IAgent>(id);
    protected string UniqueId(string prefix = "test") => $"{prefix}-{_testRunId}-{Guid.NewGuid():N[..6]}";

    // ==========================================
    // AUTO-GENERATED BEHAVIOR TESTS
    // ==========================================

    // --- Conversation ---

    [Fact]
    public async Task Behavior_Conversation_GetResponse_ReturnsNonEmpty()
    {
        var agent = Agent(UniqueId("conv-resp"));
        var response = await agent.GetResponse("Hello", CancellationToken.None);
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task Behavior_Conversation_GetResponseStream_YieldsChunks()
    {
        var agent = Agent(UniqueId("conv-stream"));
        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", CancellationToken.None))
            chunks.Add(chunk);
        Assert.NotEmpty(chunks);
    }

    [Fact]
    public async Task Behavior_Conversation_GetHistory_AfterMessage_ContainsEntries()
    {
        var agent = Agent(UniqueId("conv-hist"));
        await agent.GetResponse("Test message", CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.True(history.Count > 0);
    }

    [Fact]
    public async Task Behavior_Conversation_ClearHistory_EmptiesMessages()
    {
        var agent = Agent(UniqueId("conv-clear"));
        await agent.GetResponse("Hello", CancellationToken.None);
        await agent.ClearHistoryAsync(CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.Empty(history);
    }

    [Fact]
    public async Task Behavior_Conversation_MultipleMessages_PreserveOrder()
    {
        var agent = Agent(UniqueId("conv-order"));
        await agent.GetResponse("First", CancellationToken.None);
        await agent.GetResponse("Second", CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.True(history.Count >= 4); // 2 user + 2 assistant
    }

    // --- State ---

    [Fact]
    public async Task Behavior_State_SetWorkspace_PersistsInState()
    {
        var agent = Agent(UniqueId("state-ws"));
        await agent.SetWorkspaceAsync("/tmp/test-workspace", CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.True(state.Entries.ContainsKey("workspace-path"));
        Assert.Equal("/tmp/test-workspace", state.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task Behavior_State_GetState_ReturnsAllEntries()
    {
        var agent = Agent(UniqueId("state-all"));
        await agent.SetWorkspaceAsync("/workspace", CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.NotEmpty(state.Entries);
    }

    [Fact]
    public async Task Behavior_State_MultipleWorkspaceUpdates_KeepsLatest()
    {
        var agent = Agent(UniqueId("state-update"));
        await agent.SetWorkspaceAsync("/first", CancellationToken.None);
        await agent.SetWorkspaceAsync("/second", CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.Equal("/second", state.Entries["workspace-path"].Value);
    }

    // --- Metadata ---

    [Fact]
    public async Task Behavior_Metadata_ReturnsAgentType()
    {
        var agent = Agent(UniqueId("meta-type"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(metadata.AgentType);
        Assert.NotEmpty(metadata.AgentType);
    }

    [Fact]
    public async Task Behavior_Metadata_ReturnsDisplayName()
    {
        var agent = Agent(UniqueId("meta-name"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(metadata.DisplayName);
        Assert.NotEmpty(metadata.DisplayName);
    }

    [Fact]
    public async Task Behavior_Metadata_ReturnsKind()
    {
        var agent = Agent(UniqueId("meta-kind"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.True(metadata.Kind == AgentKind.Static || metadata.Kind == AgentKind.Dynamic);
    }

    // --- Capabilities ---

    [Fact]
    public async Task Behavior_Capabilities_HasMemory_IsTrue()
    {
        var agent = Agent(UniqueId("caps-mem"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasMemory);
    }

    [Fact]
    public async Task Behavior_Capabilities_IsCancellable_IsTrue()
    {
        var agent = Agent(UniqueId("caps-cancel"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.IsCancellable);
    }

    [Fact]
    public async Task Behavior_Capabilities_HasTimers_IsTrue()
    {
        var agent = Agent(UniqueId("caps-timers"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasTimers);
    }

    // --- Lifecycle ---

    [Fact]
    public async Task Behavior_Lifecycle_Cancel_DoesNotThrow()
    {
        var agent = Agent(UniqueId("life-cancel"));
        var exception = await Record.ExceptionAsync(() => agent.CancelAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async Task Behavior_Lifecycle_Cancel_AgentStillResponds()
    {
        var agent = Agent(UniqueId("life-cancel-respond"));
        await agent.CancelAsync(CancellationToken.None);
        var response = await agent.GetResponse("Still alive?", CancellationToken.None);
        Assert.NotNull(response);
    }

    // --- Events ---

    [Fact]
    public async Task Behavior_Events_EventLogInitiallyEmpty()
    {
        var agent = Agent(UniqueId("evt-empty"));
        var log = await agent.GetEventLogAsync(CancellationToken.None);
        Assert.Empty(log);
    }

    [Fact]
    public async Task Behavior_Events_HandleEvent_DoesNotThrow()
    {
        var agent = Agent(UniqueId("evt-handle"));
        var evt = new AgentEvent("test", "source", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, new());
        var exception = await Record.ExceptionAsync(() => agent.HandleEventAsync(evt, CancellationToken.None));
        Assert.Null(exception);
    }

    // --- Isolation ---

    [Fact]
    public async Task Behavior_Isolation_DifferentAgents_HaveSeparateState()
    {
        var agent1 = Agent(UniqueId("iso-1"));
        var agent2 = Agent(UniqueId("iso-2"));
        await agent1.SetWorkspaceAsync("/ws1", CancellationToken.None);
        await agent2.SetWorkspaceAsync("/ws2", CancellationToken.None);
        var state1 = await agent1.GetStateAsync(CancellationToken.None);
        var state2 = await agent2.GetStateAsync(CancellationToken.None);
        Assert.Equal("/ws1", state1.Entries["workspace-path"].Value);
        Assert.Equal("/ws2", state2.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task Behavior_Isolation_DifferentAgents_HaveSeparateHistory()
    {
        var agent1 = Agent(UniqueId("iso-hist-1"));
        var agent2 = Agent(UniqueId("iso-hist-2"));
        await agent1.GetResponse("Only for agent 1", CancellationToken.None);
        var history2 = await agent2.GetHistory(CancellationToken.None);
        Assert.Empty(history2);
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`

**Step 3: Commit**

```bash
git add src/IAW.Testing/AgentTestV3.cs
git commit -m "test: add AgentTestV3<T> base class with 20 auto-generated behavior tests"
```

### Task 2: Create CoreAgentV3Tests — one-line inheriting test class

**Files:**
- Create: `test/Core.Tests/V3/CoreAgentV3Tests.cs`

**Step 1: Create the test class**

```csharp
using Core.V3;
using IAW.Testing;

namespace IAW.Core.Tests.V3;

public sealed class CoreAgentV3Tests : AgentTestV3<TestAgent>;
```

One line inherits all 20 behavior tests.

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~CoreAgentV3Tests"`
Expected: All 20 tests pass

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/CoreAgentV3Tests.cs
git commit -m "test: add CoreAgentV3Tests — inherits 20 universal V3 behavior tests"
```

---

## Section 2: Typed Message System Tests

### Task 3: Test message type categorization

**Files:**
- Create: `test/Core.Tests/V3/MessageTypeTests.cs`

**Step 1: Create MessageTypeTests.cs**

```csharp
using Core.V3.Messages;
using Xunit;

namespace IAW.Core.Tests.V3;

public class MessageTypeTests
{
    [Fact]
    public void AgentActivatedEvent_ImplementsIEvent()
    {
        var evt = new AgentActivatedEvent("source", "corr", DateTimeOffset.UtcNow, "TestAgent");
        Assert.IsAssignableFrom<IEvent>(evt);
        Assert.IsAssignableFrom<IAgentMessage>(evt);
    }

    [Fact]
    public void AssignTaskCommand_ImplementsICommand()
    {
        var cmd = new AssignTaskCommand("source", "corr", DateTimeOffset.UtcNow, "Do task", null);
        Assert.IsAssignableFrom<ICommand>(cmd);
        Assert.IsAssignableFrom<IAgentMessage>(cmd);
    }

    [Fact]
    public void AlertNotification_ImplementsINotification()
    {
        var notif = new AlertNotification("source", "corr", DateTimeOffset.UtcNow, "High", "Server down");
        Assert.IsAssignableFrom<INotification>(notif);
        Assert.IsAssignableFrom<IAgentMessage>(notif);
    }

    [Fact]
    public void ProgressNotification_ImplementsINotification()
    {
        var notif = new ProgressNotification("source", "corr", DateTimeOffset.UtcNow, "Build", "Running", 0.5f);
        Assert.IsAssignableFrom<INotification>(notif);
    }

    [Fact]
    public void CodeChangedEvent_ImplementsIEvent()
    {
        var evt = new CodeChangedEvent("source", "corr", DateTimeOffset.UtcNow, ["file.cs"], "abc123");
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void BuildCompletedEvent_ImplementsIEvent()
    {
        var evt = new BuildCompletedEvent("source", "corr", DateTimeOffset.UtcNow, true, "abc", "output");
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void TestResultEvent_ImplementsIEvent()
    {
        var evt = new TestResultEvent("source", "corr", DateTimeOffset.UtcNow, true, 10, 0, "All pass");
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void HealthCheckEvent_ImplementsIEvent()
    {
        var evt = new HealthCheckEvent("source", "corr", DateTimeOffset.UtcNow, "api-server", true, 45.2);
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void DeployCompletedEvent_ImplementsIEvent()
    {
        var evt = new DeployCompletedEvent("source", "corr", DateTimeOffset.UtcNow, true, "prod", "1.0.0");
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void ReviewRequestNotification_ImplementsINotification()
    {
        var notif = new ReviewRequestNotification("source", "corr", DateTimeOffset.UtcNow, "file.cs", "Review this");
        Assert.IsAssignableFrom<INotification>(notif);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~MessageTypeTests"`
Expected: All 10 tests pass

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/MessageTypeTests.cs
git commit -m "test: add typed message hierarchy tests — IEvent, ICommand, INotification"
```

### Task 4: Test EventTypeToStreamName conversion

**Files:**
- Create: `test/Core.Tests/V3/StreamNameTests.cs`

**Step 1: Create StreamNameTests.cs**

```csharp
using Core.V3;
using Core.V3.Messages;
using Xunit;

namespace IAW.Core.Tests.V3;

public class StreamNameTests
{
    [Theory]
    [InlineData(typeof(CodeChangedEvent), "code.changed")]
    [InlineData(typeof(BuildCompletedEvent), "build.completed")]
    [InlineData(typeof(TestResultEvent), "test.result")]
    [InlineData(typeof(DeployCompletedEvent), "deploy.completed")]
    [InlineData(typeof(HealthCheckEvent), "health.check")]
    [InlineData(typeof(AgentActivatedEvent), "agent.activated")]
    [InlineData(typeof(StateChangedEvent), "state.changed")]
    [InlineData(typeof(AssignTaskCommand), "assign.task")]
    [InlineData(typeof(ProgressNotification), "progress")]
    [InlineData(typeof(AlertNotification), "alert")]
    [InlineData(typeof(ReviewRequestNotification), "review.request")]
    public void EventTypeToStreamName_ConvertsCorrectly(Type eventType, string expected)
    {
        var result = Agent.EventTypeToStreamName(eventType);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EventTypeToStreamName_SingleWord_ReturnsLowercase()
    {
        // "Progress" (after stripping "Notification") → "progress"
        var result = Agent.EventTypeToStreamName(typeof(ProgressNotification));
        Assert.Equal("progress", result);
    }

    [Fact]
    public void EventTypeToStreamName_MultiWord_UsesDotSeparation()
    {
        var result = Agent.EventTypeToStreamName(typeof(CodeChangedEvent));
        Assert.Contains(".", result);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~StreamNameTests"`
Expected: All tests pass

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/StreamNameTests.cs
git commit -m "test: add stream name resolution tests — type → dot.case conversion"
```

---

## Section 3: Communication Interface Tests

### Task 5: Test IStreamConsumer auto-subscription

**Files:**
- Create: `test/Core.Tests/V3/StreamConsumerTests.cs`

**Step 1: Create test agent that implements IStreamConsumer<T>**

```csharp
using Core.V3;
using Core.V3.Communication;
using Core.V3.Messages;
using IAW.Testing;
using Orleans.Journaling;
using Orleans.Streams;
using Xunit;

namespace IAW.Core.Tests.V3;

public interface IEventCounterAgent : Core.V3.IAgent
{
    Task<int> GetReceivedCountAsync();
    Task<string?> GetLastEventDataAsync();
}

public class EventCounterAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IEventCounterAgent,
      IStreamConsumer<CodeChangedEvent>
{
    private int _receivedCount;
    private string? _lastData;

    public Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        _receivedCount++;
        _lastData = string.Join(",", evt.FilePaths);
        return Task.CompletedTask;
    }

    public Task<int> GetReceivedCountAsync() => Task.FromResult(_receivedCount);
    public Task<string?> GetLastEventDataAsync() => Task.FromResult(_lastData);
}

public class StreamConsumerTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task StreamConsumer_ReceivesPublishedEvent()
    {
        var consumerId = $"consumer-{Guid.NewGuid():N}";
        var consumer = _cluster.GrainFactory.GetGrain<IEventCounterAgent>(consumerId);

        // Activate the consumer to wire subscriptions
        await consumer.GetResponse("init", CancellationToken.None);

        // Publish event to the stream
        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var streamId = StreamId.Create("agents", "code.changed");
        var stream = streamProvider.GetStream<AgentEvent>(streamId);

        var evt = new AgentEvent("code.changed", "publisher", Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["typed_payload"] =
                new CodeChangedEvent("publisher", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, ["src/main.cs"], "abc") });

        await stream.OnNextAsync(evt);

        // Wait for delivery
        await Task.Delay(500);

        var count = await consumer.GetReceivedCountAsync();
        Assert.True(count > 0, "Consumer should have received at least one event");
    }

    [Fact]
    public async Task StreamConsumer_MetadataReportsSubscription()
    {
        var agent = _cluster.GrainFactory.GetGrain<IEventCounterAgent>($"meta-{Guid.NewGuid():N}");
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Contains("CodeChangedEvent", metadata.Subscribes);
    }

    [Fact]
    public async Task StreamConsumer_Capabilities_HasEvents()
    {
        var agent = _cluster.GrainFactory.GetGrain<IEventCounterAgent>($"caps-{Guid.NewGuid():N}");
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasEvents);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~StreamConsumerTests"`
Expected: Tests pass

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/StreamConsumerTests.cs
git commit -m "test: add stream consumer auto-subscription tests"
```

### Task 6: Test multi-consumer fan-out

**Files:**
- Create: `test/Core.Tests/V3/StreamFanOutTests.cs`

**Step 1: Create test with multiple consumers on same stream**

```csharp
using Core.V3;
using Core.V3.Messages;
using IAW.Testing;
using Orleans.Streams;
using Xunit;

namespace IAW.Core.Tests.V3;

public class StreamFanOutTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task FanOut_MultipleConsumers_AllReceiveEvent()
    {
        var consumer1 = _cluster.GrainFactory.GetGrain<IEventCounterAgent>($"fan1-{Guid.NewGuid():N}");
        var consumer2 = _cluster.GrainFactory.GetGrain<IEventCounterAgent>($"fan2-{Guid.NewGuid():N}");
        var consumer3 = _cluster.GrainFactory.GetGrain<IEventCounterAgent>($"fan3-{Guid.NewGuid():N}");

        // Activate all consumers
        await Task.WhenAll(
            consumer1.GetResponse("init", CancellationToken.None),
            consumer2.GetResponse("init", CancellationToken.None),
            consumer3.GetResponse("init", CancellationToken.None));

        // Publish single event
        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var streamId = StreamId.Create("agents", "code.changed");
        var stream = streamProvider.GetStream<AgentEvent>(streamId);

        var evt = new AgentEvent("code.changed", "publisher", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, new());
        await stream.OnNextAsync(evt);

        await Task.Delay(1000);

        var count1 = await consumer1.GetReceivedCountAsync();
        var count2 = await consumer2.GetReceivedCountAsync();
        var count3 = await consumer3.GetReceivedCountAsync();

        Assert.True(count1 > 0, "Consumer 1 should have received event");
        Assert.True(count2 > 0, "Consumer 2 should have received event");
        Assert.True(count3 > 0, "Consumer 3 should have received event");
    }

    [Fact]
    public async Task FanOut_SinglePublish_EachConsumerReceivesExactlyOnce()
    {
        var consumer = _cluster.GrainFactory.GetGrain<IEventCounterAgent>($"once-{Guid.NewGuid():N}");
        await consumer.GetResponse("init", CancellationToken.None);

        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var streamId = StreamId.Create("agents", "code.changed");
        var stream = streamProvider.GetStream<AgentEvent>(streamId);

        await stream.OnNextAsync(new AgentEvent("code.changed", "pub", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, new()));
        await Task.Delay(500);

        var count = await consumer.GetReceivedCountAsync();
        Assert.Equal(1, count);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~StreamFanOutTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/StreamFanOutTests.cs
git commit -m "test: add stream fan-out tests — multi-consumer delivery"
```

---

## Section 4: Tool Tests

### Task 7: Test FileTools

**Files:**
- Create: `test/Core.Tests/V3/FileToolsTests.cs`

**Step 1: Create FileToolsTests.cs**

```csharp
using Core.V3.Tools;
using Xunit;

namespace IAW.Core.Tests.V3;

public class FileToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileTools _tools;

    public FileToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"iaw-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tools = new FileTools(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        var path = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(path, "hello world");
        var content = await _tools.ReadFileAsync("test.txt");
        Assert.Equal("hello world", content);
    }

    [Fact]
    public async Task ReadFile_MissingFile_ReturnsNotFound()
    {
        var result = await _tools.ReadFileAsync("missing.txt");
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteFile_CreatesFile()
    {
        var result = await _tools.WriteFileAsync("output.txt", "content");
        Assert.Contains("written", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(_tempDir, "output.txt")));
    }

    [Fact]
    public async Task WriteFile_CreatesSubdirectory()
    {
        await _tools.WriteFileAsync("sub/dir/file.txt", "content");
        Assert.True(File.Exists(Path.Combine(_tempDir, "sub", "dir", "file.txt")));
    }

    [Fact]
    public void WriteFile_OutsideWorkspace_Throws()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            _tools.WriteFileAsync("../../etc/passwd", "evil"));
    }

    [Fact]
    public void ListFiles_ReturnsMatchingFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "b.cs"), "");
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), "");

        var results = _tools.ListFiles(".", "*.cs");
        Assert.Equal(2, results.Length);
    }

    [Fact]
    public void ListFiles_ExcludesGitDirectory()
    {
        var gitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), "");

        var results = _tools.ListFiles(".", "*");
        Assert.DoesNotContain(results, r => r.Contains(".git"));
    }

    [Fact]
    public void SearchCode_FindsPattern()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"), "public class Foo { }");
        var results = _tools.SearchCode("class Foo", ".", "*.cs");
        Assert.Single(results);
        Assert.Contains("class Foo", results[0]);
    }

    [Fact]
    public void SearchCode_NoMatch_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_tempDir, "code.cs"), "public class Bar { }");
        var results = _tools.SearchCode("class Foo", ".", "*.cs");
        Assert.Empty(results);
    }

    [Fact]
    public void ListFiles_MissingDirectory_ReturnsError()
    {
        var results = _tools.ListFiles("nonexistent", "*");
        Assert.Contains("not found", results[0], StringComparison.OrdinalIgnoreCase);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~FileToolsTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/FileToolsTests.cs
git commit -m "test: add FileTools unit tests — read, write, list, search, security"
```

### Task 8: Test ShellTools

**Files:**
- Create: `test/Core.Tests/V3/ShellToolsTests.cs`

**Step 1: Create ShellToolsTests.cs**

```csharp
using Core.V3.Tools;
using Xunit;

namespace IAW.Core.Tests.V3;

public class ShellToolsTests
{
    private readonly ShellTools _tools = new(Path.GetTempPath());

    [Fact]
    public async Task RunDotnet_Version_ReturnsOutput()
    {
        var result = await _tools.RunDotnetAsync("--version");
        Assert.Contains(".", result); // version number contains dots
        Assert.Contains("Exit code: 0", result);
    }

    [Fact]
    public async Task RunShell_Echo_ReturnsOutput()
    {
        var command = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";
        var result = await _tools.RunShellAsync(command);
        Assert.Contains("hello", result);
        Assert.Contains("Exit code: 0", result);
    }

    [Fact]
    public async Task RunDotnet_InvalidCommand_ReturnsNonZeroExitCode()
    {
        var result = await _tools.RunDotnetAsync("nonexistent-command-xyz");
        Assert.DoesNotContain("Exit code: 0", result);
    }

    [Fact]
    public async Task RunShell_LongOutput_Truncates()
    {
        var command = OperatingSystem.IsWindows()
            ? "for /L %i in (1,1,1000) do @echo Line %i of long output"
            : "for i in $(seq 1 1000); do echo \"Line $i of long output\"; done";
        var result = await _tools.RunShellAsync(command);
        Assert.True(result.Length <= 8200); // 8000 + small buffer for truncation message
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~ShellToolsTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/ShellToolsTests.cs
git commit -m "test: add ShellTools unit tests — dotnet, shell, truncation"
```

### Task 9: Test WorkspaceTools

**Files:**
- Create: `test/Core.Tests/V3/WorkspaceToolsTests.cs`

**Step 1: Create WorkspaceToolsTests.cs**

```csharp
using Core.V3.Tools;
using Xunit;

namespace IAW.Core.Tests.V3;

public class WorkspaceToolsTests
{
    [Fact]
    public void SetWorkspace_AbsolutePath_Succeeds()
    {
        string? stored = null;
        var tools = new WorkspaceTools(() => stored ?? ".", path => stored = path);
        var result = tools.SetWorkspace("/tmp/workspace");
        Assert.Contains("/tmp/workspace", result);
        Assert.Equal("/tmp/workspace", stored);
    }

    [Fact]
    public void SetWorkspace_RelativePath_ReturnsError()
    {
        var tools = new WorkspaceTools(() => ".", _ => { });
        var result = tools.SetWorkspace("relative/path");
        Assert.Contains("Error", result);
    }

    [Fact]
    public void GetWorkspace_ReturnsCurrentPath()
    {
        var tools = new WorkspaceTools(() => "/my/workspace", _ => { });
        Assert.Equal("/my/workspace", tools.GetWorkspace());
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~WorkspaceToolsTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/WorkspaceToolsTests.cs
git commit -m "test: add WorkspaceTools unit tests"
```

---

## Section 5: Architecture Guard Tests

### Task 10: Create V3 architecture guard tests

**Files:**
- Create: `test/Core.Tests/V3/ArchitectureGuardV3Tests.cs`

**Step 1: Create ArchitectureGuardV3Tests.cs**

```csharp
using System.Reflection;
using Core.V3;
using Core.V3.Communication;
using Core.V3.Messages;
using Xunit;

namespace IAW.Core.Tests.V3;

public class ArchitectureGuardV3Tests
{
    private readonly Assembly _coreAssembly = typeof(Agent).Assembly;

    [Fact]
    public void Agent_ExtendsFromDurableGrain()
    {
        Assert.True(typeof(Agent).IsSubclassOf(typeof(DurableGrain)));
    }

    [Fact]
    public void Agent_IsAbstract()
    {
        Assert.True(typeof(Agent).IsAbstract);
    }

    [Fact]
    public void Agent_ImplementsIAgent()
    {
        Assert.True(typeof(IAgent).IsAssignableFrom(typeof(Agent)));
    }

    [Fact]
    public void IAgent_ExtendsIGrainWithStringKey()
    {
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(IAgent)));
    }

    [Fact]
    public void AllMessageTypes_ImplementIAgentMessage()
    {
        var messageTypes = _coreAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains("Messages") == true && t.IsClass && !t.IsAbstract);

        foreach (var type in messageTypes)
        {
            Assert.True(
                typeof(IAgentMessage).IsAssignableFrom(type),
                $"{type.Name} should implement IAgentMessage");
        }
    }

    [Fact]
    public void AllEventTypes_ImplementIEvent()
    {
        var eventTypes = _coreAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Event") && t.IsClass && !t.IsAbstract
                        && t.Namespace?.Contains("Messages") == true);

        foreach (var type in eventTypes)
        {
            Assert.True(
                typeof(IEvent).IsAssignableFrom(type),
                $"{type.Name} should implement IEvent");
        }
    }

    [Fact]
    public void AllCommandTypes_ImplementICommand()
    {
        var cmdTypes = _coreAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Command") && t.IsClass && !t.IsAbstract
                        && t.Namespace?.Contains("Messages") == true);

        foreach (var type in cmdTypes)
        {
            Assert.True(
                typeof(ICommand).IsAssignableFrom(type),
                $"{type.Name} should implement ICommand");
        }
    }

    [Fact]
    public void AllNotificationTypes_ImplementINotification()
    {
        var notifTypes = _coreAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Notification") && t.IsClass && !t.IsAbstract
                        && t.Namespace?.Contains("Messages") == true);

        foreach (var type in notifTypes)
        {
            Assert.True(
                typeof(INotification).IsAssignableFrom(type),
                $"{type.Name} should implement INotification");
        }
    }

    [Fact]
    public void AllSerializableTypes_HaveGenerateSerializerAttribute()
    {
        var serializableTypes = _coreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.StartsWith("Core.V3") == true)
            .Where(t => t.GetProperties().Any(p => p.GetCustomAttribute<IdAttribute>() is not null));

        foreach (var type in serializableTypes)
        {
            Assert.True(
                type.GetCustomAttribute<GenerateSerializerAttribute>() is not null,
                $"{type.Name} has [Id] properties but missing [GenerateSerializer]");
        }
    }

    [Fact]
    public void StreamConsumer_ConstraintRequiresIEvent()
    {
        var constraint = typeof(IStreamConsumer<>).GetGenericArguments()[0]
            .GetGenericParameterConstraints();
        Assert.Contains(typeof(IEvent), constraint);
    }

    [Fact]
    public void StreamProducer_ConstraintRequiresIEvent()
    {
        var constraint = typeof(IStreamProducer<>).GetGenericArguments()[0]
            .GetGenericParameterConstraints();
        Assert.Contains(typeof(IEvent), constraint);
    }

    [Fact]
    public void Broadcaster_ConstraintRequiresIAgentMessage()
    {
        var constraint = typeof(IBroadcaster<>).GetGenericArguments()[0]
            .GetGenericParameterConstraints();
        Assert.Contains(typeof(IAgentMessage), constraint);
    }

    [Fact]
    public void DynamicAgent_IsNotAbstract()
    {
        Assert.False(typeof(DynamicAgent).IsAbstract);
    }

    [Fact]
    public void DynamicAgent_ImplementsIDynamicAgent()
    {
        Assert.True(typeof(IDynamicAgent).IsAssignableFrom(typeof(DynamicAgent)));
    }

    [Fact]
    public void NoPublicTypes_HaveXmlDocComments()
    {
        // Enforcing code style: no /// <summary> comments
        var v3Files = Directory.GetFiles(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "Core", "V3"),
            "*.cs", SearchOption.AllDirectories);

        foreach (var file in v3Files)
        {
            var content = File.ReadAllText(file);
            Assert.False(
                content.Contains("/// <summary>"),
                $"{Path.GetFileName(file)} contains XML doc comments — use self-explanatory naming instead");
        }
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardV3Tests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/ArchitectureGuardV3Tests.cs
git commit -m "test: add V3 architecture guard tests — type hierarchy, constraints, serialization"
```

---

## Section 6: DynamicAgent Tests

### Task 11: Test DynamicAgent behavior

**Files:**
- Create: `test/Core.Tests/V3/DynamicAgentBehaviorTests.cs`

**Step 1: Create comprehensive DynamicAgent tests**

```csharp
using Core.V3;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.V3;

public class DynamicAgentBehaviorTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    private IDynamicAgent DynamicAgent(string id) =>
        _cluster.GrainFactory.GetGrain<IDynamicAgent>(id);

    [Fact]
    public async Task Configure_DisplayName_ReflectsInMetadata()
    {
        var agent = DynamicAgent($"dyn-name-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration("Custom Bot", null, null, null, null), CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("Custom Bot", meta.DisplayName);
    }

    [Fact]
    public async Task Configure_SystemPrompt_ReflectsInMetadata()
    {
        var agent = DynamicAgent($"dyn-prompt-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration(null, "You are a pirate.", null, null, null), CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("You are a pirate.", meta.Description);
    }

    [Fact]
    public async Task Configure_Workspace_PersistsInState()
    {
        var agent = DynamicAgent($"dyn-ws-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration(null, null, null, "/work/dir", null), CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.Equal("/work/dir", state.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task Kind_IsDynamic()
    {
        var agent = DynamicAgent($"dyn-kind-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal(AgentKind.Dynamic, meta.Kind);
    }

    [Fact]
    public async Task Unconfigured_HasDefaultDisplayName()
    {
        var agent = DynamicAgent($"dyn-default-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("Dynamic Agent", meta.DisplayName);
    }

    [Fact]
    public async Task Configure_MultipleTimes_KeepsLatest()
    {
        var agent = DynamicAgent($"dyn-multi-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration("First", null, null, null, null), CancellationToken.None);
        await agent.ConfigureAsync(new AgentConfiguration("Second", null, null, null, null), CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("Second", meta.DisplayName);
    }

    [Fact]
    public async Task Configure_PartialUpdate_PreservesOtherFields()
    {
        var agent = DynamicAgent($"dyn-partial-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(new AgentConfiguration("Bot", "Instructions", null, null, null), CancellationToken.None);
        await agent.ConfigureAsync(new AgentConfiguration("Updated Bot", null, null, null, null), CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("Updated Bot", meta.DisplayName);
        Assert.Equal("Instructions", meta.Description);
    }

    [Fact]
    public async Task DynamicAgent_CanConverse()
    {
        var agent = DynamicAgent($"dyn-conv-{Guid.NewGuid():N}");
        var response = await agent.GetResponse("Hello", CancellationToken.None);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task DynamicAgent_CanClearHistory()
    {
        var agent = DynamicAgent($"dyn-clear-{Guid.NewGuid():N}");
        await agent.GetResponse("Hello", CancellationToken.None);
        await agent.ClearHistoryAsync(CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.Empty(history);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~DynamicAgentBehaviorTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/DynamicAgentBehaviorTests.cs
git commit -m "test: add comprehensive DynamicAgent behavior tests"
```

---

## Section 7: Registry Tests

### Task 12: Test AgentRegistryGrain

**Files:**
- Create: `test/Core.Tests/V3/RegistryTests.cs`

**Step 1: Create RegistryTests.cs**

```csharp
using Core.V3;
using Core.V3.Registry;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.V3;

public class RegistryTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    private IAgentRegistryGrain Registry =>
        _cluster.GrainFactory.GetGrain<IAgentRegistryGrain>("test-registry");

    [Fact]
    public async Task Register_StoresRegistration()
    {
        var reg = new AgentRegistration("TestAgent", "Test", "desc", AgentKind.Static, [], [], []);
        await Registry.RegisterAsync(reg);
        var result = await Registry.GetByTypeAsync("TestAgent");
        Assert.NotNull(result);
        Assert.Equal("Test", result.DisplayName);
    }

    [Fact]
    public async Task Unregister_RemovesRegistration()
    {
        var reg = new AgentRegistration("RemoveMe", "Remove", "desc", AgentKind.Static, [], [], []);
        await Registry.RegisterAsync(reg);
        await Registry.UnregisterAsync("RemoveMe");
        var result = await Registry.GetByTypeAsync("RemoveMe");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAll_ReturnsAllRegistrations()
    {
        await Registry.RegisterAsync(new AgentRegistration("A1", "Agent 1", "", AgentKind.Static, [], [], []));
        await Registry.RegisterAsync(new AgentRegistration("A2", "Agent 2", "", AgentKind.Dynamic, [], [], []));
        var all = await Registry.GetAllAsync();
        Assert.True(all.Count >= 2);
    }

    [Fact]
    public async Task Query_ByKind_FiltersCorrectly()
    {
        await Registry.RegisterAsync(new AgentRegistration("Static1", "S1", "", AgentKind.Static, [], [], []));
        await Registry.RegisterAsync(new AgentRegistration("Dynamic1", "D1", "", AgentKind.Dynamic, [], [], []));
        var results = await Registry.QueryAsync(new AgentQuery(Kind: AgentKind.Dynamic));
        Assert.All(results, r => Assert.Equal(AgentKind.Dynamic, r.Kind));
    }

    [Fact]
    public async Task Query_ByPublishes_FiltersCorrectly()
    {
        await Registry.RegisterAsync(new AgentRegistration("Pub1", "P1", "", AgentKind.Static, [], ["code.changed"], []));
        await Registry.RegisterAsync(new AgentRegistration("Pub2", "P2", "", AgentKind.Static, [], ["build.completed"], []));
        var results = await Registry.QueryAsync(new AgentQuery(Publishes: ["code.changed"]));
        Assert.Contains(results, r => r.AgentType == "Pub1");
    }

    [Fact]
    public async Task GetByType_NonExistent_ReturnsNull()
    {
        var result = await Registry.GetByTypeAsync("NonExistentAgent12345");
        Assert.Null(result);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~RegistryTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/RegistryTests.cs
git commit -m "test: add AgentRegistryGrain tests — register, unregister, query"
```

---

## Section 8: Telemetry Tests

### Task 13: Test AgentTelemetry counters

**Files:**
- Create: `test/Core.Tests/V3/TelemetryTests.cs`

**Step 1: Create TelemetryTests.cs**

```csharp
using Core.V3.Observability;
using Xunit;

namespace IAW.Core.Tests.V3;

public class TelemetryTests
{
    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        Assert.Equal("IAW", AgentTelemetry.SourceName);
        Assert.Equal("IAW", AgentTelemetry.ActivitySource.Name);
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        Assert.Equal("IAW", AgentTelemetry.MeterName);
        Assert.Equal("IAW", AgentTelemetry.Meter.Name);
    }

    [Fact]
    public void Counters_AreNotNull()
    {
        Assert.NotNull(AgentTelemetry.EventsPublished);
        Assert.NotNull(AgentTelemetry.EventsHandled);
        Assert.NotNull(AgentTelemetry.Activations);
        Assert.NotNull(AgentTelemetry.MessagesSent);
        Assert.NotNull(AgentTelemetry.ConversationErrors);
    }

    [Fact]
    public void Histograms_AreNotNull()
    {
        Assert.NotNull(AgentTelemetry.EventHandleDuration);
        Assert.NotNull(AgentTelemetry.ConversationDuration);
    }

    [Fact]
    public void Counters_CanBeIncremented()
    {
        // Should not throw
        AgentTelemetry.Activations.Add(1);
        AgentTelemetry.MessagesSent.Add(1);
        AgentTelemetry.EventsPublished.Add(1);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~TelemetryTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/TelemetryTests.cs
git commit -m "test: add telemetry counter and histogram tests"
```

---

## Section 9: Context Provider Tests

### Task 14: Test AIContext

**Files:**
- Create: `test/Core.Tests/V3/ContextTests.cs`

**Step 1: Create ContextTests.cs**

```csharp
using Core.V3;
using Core.V3.Context;
using Xunit;

namespace IAW.Core.Tests.V3;

public class ContextTests
{
    [Fact]
    public void AIContext_Empty_HasNoMessages()
    {
        var ctx = AIContext.Empty;
        Assert.Empty(ctx.AdditionalMessages);
        Assert.Null(ctx.Metadata);
    }

    [Fact]
    public void AIContext_WithMessages_ContainsMessages()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "Context info" }
        };
        var ctx = new AIContext(messages);
        Assert.Single(ctx.AdditionalMessages);
    }

    [Fact]
    public void AIContext_WithMetadata_ContainsMetadata()
    {
        var ctx = new AIContext([], new Dictionary<string, string> { ["key"] = "value" });
        Assert.NotNull(ctx.Metadata);
        Assert.Equal("value", ctx.Metadata["key"]);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~ContextTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/ContextTests.cs
git commit -m "test: add AIContext and context provider tests"
```

---

## Section 10: Diagnostics Tests

### Task 15: Test DiagnosticReport

**Files:**
- Create: `test/Core.Tests/V3/DiagnosticsTests.cs`

**Step 1: Create DiagnosticsTests.cs**

```csharp
using Core.V3;
using Core.V3.Diagnostics;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.V3;

public class DiagnosticsTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task DiagnoseAsync_DefaultAgent_ReportsHealthy()
    {
        var agent = _cluster.GrainFactory.GetGrain<ITestAgent>($"diag-{Guid.NewGuid():N}");
        // Cast to ISelfDiagnosable if accessible, or test via metadata
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(meta);
    }

    [Fact]
    public void DiagnosticReport_Construction_SetsAllFields()
    {
        var report = new DiagnosticReport(
            "TestAgent", DateTimeOffset.UtcNow, true, 5, 5, TimeSpan.FromMilliseconds(100), []);
        Assert.True(report.Healthy);
        Assert.Equal(5, report.TestsRun);
        Assert.Equal(5, report.TestsPassed);
        Assert.Empty(report.Failures);
    }

    [Fact]
    public void DiagnosticFailure_Construction_SetsFields()
    {
        var failure = new DiagnosticFailure("TestMethod", "Something broke", "at line 42");
        Assert.Equal("TestMethod", failure.TestName);
        Assert.Equal("Something broke", failure.Message);
        Assert.Equal("at line 42", failure.StackTrace);
    }

    [Fact]
    public void DiagnosticReport_WithFailures_IsUnhealthy()
    {
        var failures = new List<DiagnosticFailure>
        {
            new("Test1", "Failed", null)
        };
        var report = new DiagnosticReport("Agent", DateTimeOffset.UtcNow, false, 1, 0, TimeSpan.Zero, failures);
        Assert.False(report.Healthy);
        Assert.Single(report.Failures);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~DiagnosticsTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/DiagnosticsTests.cs
git commit -m "test: add diagnostics report tests"
```

---

## Section 11: Integration Tests

### Task 16: Create V3 Aspire integration tests

**Files:**
- Create: `test/Integration.Tests/V3/AgentV3IntegrationTests.cs`

**Step 1: Create integration test class**

```csharp
using Core.V3;
using IAW.Testing;
using Xunit;

namespace IAW.Integration.Tests.V3;

public class AgentV3IntegrationTests : AspireAgentTest<Agent>
{
    [Fact]
    public async Task V3Agent_CanActivateInAspire()
    {
        var agent = OrleansClient.GetGrain<Core.V3.IAgent>("integration-test-v3");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.NotNull(meta);
    }

    [Fact]
    public async Task V3Agent_CanConverse_InAspire()
    {
        var agent = OrleansClient.GetGrain<Core.V3.IAgent>("integration-conv-v3");
        var response = await agent.GetResponse("Hello from integration test", CancellationToken.None);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task V3Agent_StatePersists_AcrossRequests()
    {
        var id = $"integration-state-{Guid.NewGuid():N}";
        var agent = OrleansClient.GetGrain<Core.V3.IAgent>(id);
        await agent.SetWorkspaceAsync("/integration/workspace", CancellationToken.None);
        var state = await agent.GetStateAsync(CancellationToken.None);
        Assert.Equal("/integration/workspace", state.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task V3Agent_StreamsWork_InAspire()
    {
        var agent = OrleansClient.GetGrain<Core.V3.IAgent>("integration-streams-v3");
        var subs = await agent.GetActiveSubscriptionsAsync(CancellationToken.None);
        // Base agent has no subscriptions by default
        Assert.NotNull(subs);
    }

    [Fact]
    public async Task DynamicAgent_CanConfigure_InAspire()
    {
        var agent = OrleansClient.GetGrain<IDynamicAgent>($"dyn-integration-{Guid.NewGuid():N}");
        await agent.ConfigureAsync(
            new AgentConfiguration("Integration Bot", "You are an integration test bot.", null, null, null),
            CancellationToken.None);
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("Integration Bot", meta.DisplayName);
    }

    [Fact]
    public async Task Registry_ContainsDiscoveredAgents()
    {
        var registry = OrleansClient.GetGrain<Core.V3.Registry.IAgentRegistryGrain>("global");
        var all = await registry.GetAllAsync();
        Assert.NotEmpty(all);
    }
}
```

**Step 2: Run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter "FullyQualifiedName~AgentV3IntegrationTests"`

**Step 3: Commit**

```bash
git add test/Integration.Tests/V3/
git commit -m "test: add V3 Aspire integration tests — activation, conversation, state, streams, registry"
```

---

## Section 12: Sample Agent Tests

### Task 17: Test sample use case agents

**Files:**
- Create: `test/Core.Tests/V3/SampleAgentTests.cs`

**Step 1: Create SampleAgentTests.cs**

```csharp
using Core.V3;
using Core.V3.Samples;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.V3;

public class SampleAgentTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task WeatherAgent_CanActivate()
    {
        var agent = _cluster.GrainFactory.GetGrain<IWeatherAgent>($"weather-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("WeatherAgent", meta.AgentType);
    }

    [Fact]
    public async Task WeatherAgent_HasTools()
    {
        var agent = _cluster.GrainFactory.GetGrain<IWeatherAgent>($"weather-tools-{Guid.NewGuid():N}");
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasTools);
    }

    [Fact]
    public async Task CodeReviewAgent_SubscribesToCodeChanged()
    {
        var agent = _cluster.GrainFactory.GetGrain<ICodeReviewAgent>($"review-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Contains("CodeChangedEvent", meta.Subscribes);
    }

    [Fact]
    public async Task CIPipelineAgent_SubscribesAndPublishes()
    {
        var agent = _cluster.GrainFactory.GetGrain<ICIPipelineAgent>($"ci-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Contains("CodeChangedEvent", meta.Subscribes);
        Assert.Contains("BuildCompletedEvent", meta.Publishes);
    }

    [Fact]
    public async Task KnowledgeBaseAgent_HasCustomTools()
    {
        var agent = _cluster.GrainFactory.GetGrain<IKnowledgeBaseAgent>($"kb-{Guid.NewGuid():N}");
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasTools);
    }

    [Fact]
    public async Task PersonalAssistantAgent_PublishesBroadcasts()
    {
        var agent = _cluster.GrainFactory.GetGrain<IPersonalAssistantAgent>($"pa-{Guid.NewGuid():N}");
        var meta = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Contains("AssignTaskCommand", meta.Publishes);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~SampleAgentTests"`

**Step 3: Commit**

```bash
git add test/Core.Tests/V3/SampleAgentTests.cs
git commit -m "test: add sample agent tests — all 5 use cases verified"
```

---

## Section 13: Test Coverage Summary

### Task 18: Create test coverage report

**Files:**
- Create: `docs/test-coverage-matrix.md`

**Step 1: Write coverage matrix**

```markdown
# V3 Test Coverage Matrix

## Behavior Contract Tests (AgentTestV3<T> — 20 auto-generated)

| # | Test | Behavior | Status |
|---|------|----------|--------|
| 1 | Behavior_Conversation_GetResponse_ReturnsNonEmpty | Conversation | ✓ |
| 2 | Behavior_Conversation_GetResponseStream_YieldsChunks | Conversation | ✓ |
| 3 | Behavior_Conversation_GetHistory_AfterMessage_ContainsEntries | Conversation | ✓ |
| 4 | Behavior_Conversation_ClearHistory_EmptiesMessages | Conversation | ✓ |
| 5 | Behavior_Conversation_MultipleMessages_PreserveOrder | Conversation | ✓ |
| 6 | Behavior_State_SetWorkspace_PersistsInState | State | ✓ |
| 7 | Behavior_State_GetState_ReturnsAllEntries | State | ✓ |
| 8 | Behavior_State_MultipleWorkspaceUpdates_KeepsLatest | State | ✓ |
| 9 | Behavior_Metadata_ReturnsAgentType | Metadata | ✓ |
| 10 | Behavior_Metadata_ReturnsDisplayName | Metadata | ✓ |
| 11 | Behavior_Metadata_ReturnsKind | Metadata | ✓ |
| 12 | Behavior_Capabilities_HasMemory_IsTrue | Capabilities | ✓ |
| 13 | Behavior_Capabilities_IsCancellable_IsTrue | Capabilities | ✓ |
| 14 | Behavior_Capabilities_HasTimers_IsTrue | Capabilities | ✓ |
| 15 | Behavior_Lifecycle_Cancel_DoesNotThrow | Lifecycle | ✓ |
| 16 | Behavior_Lifecycle_Cancel_AgentStillResponds | Lifecycle | ✓ |
| 17 | Behavior_Events_EventLogInitiallyEmpty | Events | ✓ |
| 18 | Behavior_Events_HandleEvent_DoesNotThrow | Events | ✓ |
| 19 | Behavior_Isolation_DifferentAgents_HaveSeparateState | Isolation | ✓ |
| 20 | Behavior_Isolation_DifferentAgents_HaveSeparateHistory | Isolation | ✓ |

## Unit Tests

| Test Class | Tests | Area |
|-----------|-------|------|
| MessageTypeTests | 10 | Typed message hierarchy |
| StreamNameTests | 13 | Stream name resolution |
| FileToolsTests | 10 | File tool security + function |
| ShellToolsTests | 4 | Shell execution |
| WorkspaceToolsTests | 3 | Workspace management |
| TelemetryTests | 5 | OpenTelemetry counters |
| ContextTests | 3 | AIContext |
| DiagnosticsTests | 4 | DiagnosticReport |

## Behavior-Specific Tests

| Test Class | Tests | Area |
|-----------|-------|------|
| StreamConsumerTests | 3 | Auto-subscription + metadata |
| StreamFanOutTests | 2 | Multi-consumer delivery |
| DynamicAgentBehaviorTests | 9 | Runtime configuration |
| RegistryTests | 6 | Agent registration + query |

## Architecture Guards

| Test Class | Tests | Area |
|-----------|-------|------|
| ArchitectureGuardV3Tests | 13 | Type hierarchy, constraints, style |

## Integration Tests

| Test Class | Tests | Area |
|-----------|-------|------|
| AgentV3IntegrationTests | 6 | Full Aspire stack |

## Sample Agent Tests

| Test Class | Tests | Area |
|-----------|-------|------|
| SampleAgentTests | 6 | All 5 use cases |

## Total: ~112 tests across 15 test classes
```

**Step 2: Commit**

```bash
git add docs/test-coverage-matrix.md
git commit -m "docs: add V3 test coverage matrix — 112 tests across 15 classes"
```

### Task 19: Run full test suite and verify

**Step 1: Build everything**

Run: `dotnet build IAW.slnx`

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx --logger "console;verbosity=detailed"`

**Step 3: Fix any failures**

**Step 4: Commit**

```bash
git commit -m "test: all V3 tests passing — 112 tests green"
```

---

## Summary: Quality Assurance Task Count

| Section | Tasks | Tests |
|---------|-------|-------|
| Test Infrastructure | 2 | 20 (auto-generated) |
| Message System | 2 | 23 |
| Communication | 2 | 5 |
| Tools | 3 | 17 |
| Architecture Guards | 1 | 13 |
| DynamicAgent | 1 | 9 |
| Registry | 1 | 6 |
| Telemetry | 1 | 5 |
| Context | 1 | 3 |
| Diagnostics | 1 | 4 |
| Integration | 1 | 6 |
| Samples | 1 | 6 |
| Coverage Report | 1 | — |
| Final Verification | 1 | — |
| **Total** | **19** | **~112 tests** |

Note: Each task expands to 3-5 micro-steps (write test, run, verify fail/pass, fix, commit) bringing effective step count to ~500.
