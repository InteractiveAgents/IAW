# IAW Testing Framework Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build an `IAW.Testing` NuGet package with `AgentTest<T>` and `AspireAgentTest<T>` base classes that provide universal behavior tests, a fluent scenario builder, and mock LLM support for testing IAW agents.

**Architecture:** Two base classes share a `ScenarioBuilder` fluent API. `AgentTest<T>` uses Orleans `TestCluster` (fast, in-process). `AspireAgentTest<T>` boots the full Aspire AppHost (cross-silo). Both provide `Agent(id)` to get grain references and `Scenario` for Given/When/Then orchestration. The 8 universal behavior tests are xunit `[Fact]` methods on `AgentTest<T>` that every `IAgent` inherits automatically.

**Tech Stack:** .NET 11.0, Orleans 10.0.1 TestingHost, Aspire 13.1.2 Hosting.Testing, xunit v3 3.2.2, Microsoft.Extensions.AI 10.3.0

---

### Task 1: Create IAW.Testing Project Skeleton

**Files:**
- Create: `src/IAW.Testing/IAW.Testing.csproj`

**Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Orleans.Persistence.Memory" />
    <PackageReference Include="Microsoft.Orleans.Reminders" />
    <PackageReference Include="Microsoft.Orleans.Streaming" />
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

**Step 2: Add to solution**

Run: `cd E:/IAW/InteractiveAgents/IAW && dotnet sln IAW.slnx add src/IAW.Testing/IAW.Testing.csproj`

**Step 3: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/IAW.Testing/IAW.Testing.csproj IAW.slnx
git commit -m "feat: add IAW.Testing project skeleton"
```

---

### Task 2: Create AgentTestConfigurator and WaitHelpers

**Files:**
- Create: `src/IAW.Testing/AgentTestConfigurator.cs`
- Create: `src/IAW.Testing/WaitHelpers.cs`

**Step 1: Create silo configurator**

This is the Orleans TestingHost configuration used by `AgentTest<T>`. Extracted from the existing `test/Agents.Tests/AgentsSiloConfigurator.cs`.

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

namespace IAW.Testing;

public sealed class AgentTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams("agents")
            .UseInMemoryReminderService();

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();
    }
}

public sealed class AgentTestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("agents");
    }
}
```

**Step 2: Create WaitHelpers**

```csharp
using Core;

namespace IAW.Testing;

public static class WaitHelpers
{
    public static async Task<T> WaitForAsync<T>(
        Func<Task<T>> query,
        Func<T, bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken ct = default)
    {
        var totalTimeout = timeout ?? TimeSpan.FromSeconds(5);
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(25);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(totalTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var result = await query();
            if (condition(result))
                return result;

            await Task.Delay(interval, cts.Token);
        }

        throw new TimeoutException($"Condition not met within {totalTimeout.TotalSeconds}s.");
    }

    public static async Task<AgentTrackingStatus> WaitForTrackingToStopAsync(
        IAgent agent,
        CancellationToken ct = default)
    {
        return await WaitForAsync(
            () => agent.GetTrackingStatusAsync(ct),
            status => !status.IsTracking,
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
    }

    public static async Task<List<NotificationRecord>> WaitForNotificationsAsync(
        IAgent agent,
        int expectedCount,
        CancellationToken ct = default)
    {
        return await WaitForAsync(
            () => agent.GetNotificationsAsync(ct),
            notifications => notifications.Count >= expectedCount,
            timeout: TimeSpan.FromSeconds(5),
            ct: ct);
    }
}
```

**Step 3: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/IAW.Testing/AgentTestConfigurator.cs src/IAW.Testing/WaitHelpers.cs
git commit -m "feat: add AgentTestConfigurator and WaitHelpers"
```

---

### Task 3: Create MockChatClient

**Files:**
- Create: `src/IAW.Testing/MockChatClient.cs`

**Step 1: Create the mock**

```csharp
using Microsoft.Extensions.AI;

namespace IAW.Testing;

public sealed class MockChatClient : IChatClient
{
    private readonly List<string> _receivedMessages = [];
    private Func<IList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>>? _responseFactory;
    private Func<IList<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? _streamFactory;

    public int SendCount { get; private set; }
    public IReadOnlyList<string> ReceivedMessages => _receivedMessages;

    public MockChatClient ReturnsText(string response)
    {
        _responseFactory = (_, _, _) =>
        {
            var chatMessage = new ChatMessage(ChatRole.Assistant, response);
            return Task.FromResult(new ChatResponse(chatMessage));
        };
        return this;
    }

    public MockChatClient ReturnsStream(params string[] chunks)
    {
        _streamFactory = (_, _, ct) => StreamChunksAsync(chunks, ct);
        return this;
    }

    public MockChatClient ThrowsOnSend(Exception exception)
    {
        _responseFactory = (_, _, _) => throw exception;
        _streamFactory = (_, _, _) => ThrowAsync(exception);
        return this;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SendCount++;
        RecordMessages(chatMessages);

        if (_responseFactory is not null)
            return await _responseFactory(chatMessages, options, cancellationToken);

        var message = new ChatMessage(ChatRole.Assistant, string.Empty);
        return new ChatResponse(message);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IList<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SendCount++;
        RecordMessages(chatMessages);

        if (_streamFactory is not null)
            return _streamFactory(chatMessages, options, cancellationToken);

        return EmptyStreamAsync();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private void RecordMessages(IList<ChatMessage> messages)
    {
        foreach (var msg in messages)
        {
            if (msg.Text is { Length: > 0 } text)
                _receivedMessages.Add(text);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamChunksAsync(
        string[] chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Text = chunk
            };
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyStreamAsync()
    {
        yield break;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowAsync(Exception ex)
    {
        throw ex;
        yield break;
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/IAW.Testing/MockChatClient.cs
git commit -m "feat: add MockChatClient for LLM simulation in tests"
```

---

### Task 4: Create Scenario Builder — AgentRef and StreamRef

**Files:**
- Create: `src/IAW.Testing/Scenario/AgentRef.cs`
- Create: `src/IAW.Testing/Scenario/StreamRef.cs`

**Step 1: Create AgentRef**

```csharp
using Core;

namespace IAW.Testing.Scenario;

public sealed class AgentRef(Func<string, IAgent> agentFactory, string agentId)
{
    public string AgentId { get; } = agentId;

    public IAgent Resolve() => agentFactory(AgentId);
}
```

**Step 2: Create StreamRef**

```csharp
namespace IAW.Testing.Scenario;

public sealed class StreamRef(string streamNamespace, Guid streamId)
{
    public string Namespace { get; } = streamNamespace;
    public Guid StreamId { get; } = streamId;
}
```

**Step 3: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/IAW.Testing/Scenario/
git commit -m "feat: add AgentRef and StreamRef for scenario builder"
```

---

### Task 5: Create Scenario Builder — Step Types

**Files:**
- Create: `src/IAW.Testing/Scenario/ScenarioStep.cs`

**Step 1: Create the step types**

```csharp
using Core;

namespace IAW.Testing.Scenario;

public abstract class ScenarioStep
{
    public abstract Task ExecuteAsync(CancellationToken ct);
}

// -- Given steps --

public sealed class GivenSubscribesStep(AgentRef publisher, string topic, string subscriberId) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await publisher.Resolve().SubscribeAsync(topic, subscriberId, ct);
    }
}

public sealed class GivenStateStep(AgentRef agent, string key, string value) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().SetStateAsync(key, value, ct);
    }
}

public sealed class GivenHistoryStep(AgentRef agent, string role, string content) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().AddHistoryAsync(role, content, ct);
    }
}

// -- When steps --

public sealed class WhenNotifiesStep(AgentRef agent, string topic, string payload) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().NotifyAsync(topic, payload, ct);
    }
}

public sealed class WhenNotifiesEnvelopeStep(AgentRef agent, NotificationEnvelope envelope) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().NotifyAsync(envelope, ct);
    }
}

public sealed class WhenPublishesEventStep(AgentRef agent, string name, string? payload) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().PublishEventAsync(name, payload, ct);
    }
}

public sealed class WhenPublishesStreamStep(AgentRef agent, string streamNamespace, Guid streamId, string message) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().PublishStreamAsync(streamNamespace, streamId, message, ct);
    }
}

public sealed class WhenSetsStateStep(AgentRef agent, string key, string value) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().SetStateAsync(key, value, ct);
    }
}

public sealed class WhenIncrementsStep(AgentRef agent, string key) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().IncrementAsync(key, ct);
    }
}

public sealed class WhenAddsHistoryStep(AgentRef agent, string role, string content) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await agent.Resolve().AddHistoryAsync(role, content, ct);
    }
}

// -- Then steps (assertions) --

public sealed class ThenHasNotificationStep(AgentRef agent, string topic, string payload) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var notifications = await WaitHelpers.WaitForNotificationsAsync(agent.Resolve(), 1, ct);
        var match = notifications.Find(n => n.Topic == topic && n.Payload == payload);
        if (match is null)
            throw new Xunit.Sdk.XunitException(
                $"Agent '{agent.AgentId}' has no notification with topic='{topic}' payload='{payload}'. " +
                $"Found {notifications.Count} notification(s): [{string.Join(", ", notifications.Select(n => $"{{topic={n.Topic}, payload={n.Payload}}}"))}]");
    }
}

public sealed class ThenHasNotificationMatchingStep(AgentRef agent, Func<NotificationRecord, bool> predicate) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var notifications = await WaitHelpers.WaitForAsync(
            () => agent.Resolve().GetNotificationsAsync(ct),
            list => list.Exists(predicate),
            ct: ct);
    }
}

public sealed class ThenHasEventStep(AgentRef agent, string name) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var events = await WaitHelpers.WaitForAsync(
            () => agent.Resolve().GetEventsAsync(ct),
            list => list.Exists(e => e.Name == name),
            ct: ct);
    }
}

public sealed class ThenHasStateStep(AgentRef agent, string key, string expectedValue) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var value = await agent.Resolve().GetStateValueAsync(key, ct);
        if (!string.Equals(value, expectedValue, StringComparison.Ordinal))
            throw new Xunit.Sdk.XunitException(
                $"Agent '{agent.AgentId}' state['{key}'] = '{value ?? "<null>"}', expected '{expectedValue}'.");
    }
}

public sealed class ThenHasHistoryCountStep(AgentRef agent, int expectedCount) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        var history = await agent.Resolve().GetHistoryAsync(ct);
        if (history.Count != expectedCount)
            throw new Xunit.Sdk.XunitException(
                $"Agent '{agent.AgentId}' history count = {history.Count}, expected {expectedCount}.");
    }
}

public sealed class ThenHasTrackingStatusStep(AgentRef agent, Func<AgentTrackingStatus, bool> predicate) : ScenarioStep
{
    public override async Task ExecuteAsync(CancellationToken ct)
    {
        await WaitHelpers.WaitForAsync(
            () => agent.Resolve().GetTrackingStatusAsync(ct),
            predicate,
            ct: ct);
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/IAW.Testing/Scenario/ScenarioStep.cs
git commit -m "feat: add scenario step types for Given/When/Then"
```

---

### Task 6: Create ScenarioBuilder — Fluent API

**Files:**
- Create: `src/IAW.Testing/Scenario/ScenarioBuilder.cs`

**Step 1: Create the fluent builder**

```csharp
using Core;

namespace IAW.Testing.Scenario;

public sealed class ScenarioBuilder(Func<string, IAgent> agentFactory)
{
    private readonly List<ScenarioStep> _steps = [];

    public AgentStepBuilder Given(AgentRef agent) => new(this, agent, StepPhase.Given);
    public AgentStepBuilder When(AgentRef agent) => new(this, agent, StepPhase.When);
    public AgentStepBuilder Then(AgentRef agent) => new(this, agent, StepPhase.Then);

    public AgentRef Agent(string id) => new(agentFactory, id);

    internal void AddStep(ScenarioStep step) => _steps.Add(step);

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        foreach (var step in _steps)
        {
            await step.ExecuteAsync(cts.Token);
        }
    }
}

public enum StepPhase { Given, When, Then }

public sealed class AgentStepBuilder(ScenarioBuilder scenario, AgentRef agent, StepPhase phase)
{
    // -- Given --

    public ScenarioBuilder Subscribes(string topic, string to)
    {
        scenario.AddStep(new GivenSubscribesStep(agent, topic, to));
        return scenario;
    }

    public ScenarioBuilder HasState(string key, string value)
    {
        if (phase == StepPhase.Then)
        {
            scenario.AddStep(new ThenHasStateStep(agent, key, value));
            return scenario;
        }

        scenario.AddStep(new GivenStateStep(agent, key, value));
        return scenario;
    }

    public ScenarioBuilder HasHistory(string role, string content)
    {
        scenario.AddStep(new GivenHistoryStep(agent, role, content));
        return scenario;
    }

    // -- When --

    public ScenarioBuilder Notifies(string topic, string payload)
    {
        scenario.AddStep(new WhenNotifiesStep(agent, topic, payload));
        return scenario;
    }

    public ScenarioBuilder NotifiesWithEnvelope(NotificationEnvelope envelope)
    {
        scenario.AddStep(new WhenNotifiesEnvelopeStep(agent, envelope));
        return scenario;
    }

    public ScenarioBuilder PublishesEvent(string name, string? payload = null)
    {
        scenario.AddStep(new WhenPublishesEventStep(agent, name, payload));
        return scenario;
    }

    public ScenarioBuilder PublishesStream(string streamNamespace, Guid streamId, string message)
    {
        scenario.AddStep(new WhenPublishesStreamStep(agent, streamNamespace, streamId, message));
        return scenario;
    }

    public ScenarioBuilder SetsState(string key, string value)
    {
        scenario.AddStep(new WhenSetsStateStep(agent, key, value));
        return scenario;
    }

    public ScenarioBuilder Increments(string key)
    {
        scenario.AddStep(new WhenIncrementsStep(agent, key));
        return scenario;
    }

    public ScenarioBuilder AddsHistory(string role, string content)
    {
        scenario.AddStep(new WhenAddsHistoryStep(agent, role, content));
        return scenario;
    }

    // -- Then --

    public ScenarioBuilder HasNotification(string topic, string payload)
    {
        scenario.AddStep(new ThenHasNotificationStep(agent, topic, payload));
        return scenario;
    }

    public ScenarioBuilder HasNotificationMatching(Func<NotificationRecord, bool> predicate)
    {
        scenario.AddStep(new ThenHasNotificationMatchingStep(agent, predicate));
        return scenario;
    }

    public ScenarioBuilder HasEvent(string name)
    {
        scenario.AddStep(new ThenHasEventStep(agent, name));
        return scenario;
    }

    public ScenarioBuilder HasHistory(int count)
    {
        scenario.AddStep(new ThenHasHistoryCountStep(agent, count));
        return scenario;
    }

    public ScenarioBuilder HasTrackingStatus(Func<AgentTrackingStatus, bool> predicate)
    {
        scenario.AddStep(new ThenHasTrackingStatusStep(agent, predicate));
        return scenario;
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/IAW.Testing/Scenario/ScenarioBuilder.cs
git commit -m "feat: add ScenarioBuilder fluent API with Given/When/Then"
```

---

### Task 7: Create AgentTest<T> Base Class with 8 Universal Behavior Tests

**Files:**
- Create: `src/IAW.Testing/AgentTest.cs`

**Step 1: Create the base class**

This is the core deliverable. The 8 behavior test suites are `[Fact]` methods that run automatically when a class inherits `AgentTest<T>`.

```csharp
using Core;
using IAW.Testing.Scenario;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Testing;

public abstract class AgentTest<T> : IAsyncLifetime where T : class, IAgent
{
    private TestCluster _cluster = null!;
    private string _testRunId = null!;

    protected TestCluster Cluster => _cluster;
    protected IStreamProvider StreamProvider => _cluster.Client.GetStreamProvider("agents");
    protected MockChatClient MockLlm { get; } = new();

    protected ScenarioBuilder Scenario => new(Agent);

    protected IAgent Agent(string id) => _cluster.GrainFactory.GetGrain<IAgent>(id);

    protected virtual void ConfigureSilo(ISiloBuilder builder) { }
    protected virtual Task OnClusterReadyAsync() => Task.CompletedTask;
    protected virtual Task OnBeforeTestAsync() => Task.CompletedTask;
    protected virtual Task OnAfterTestAsync() => Task.CompletedTask;
    protected virtual Task OnAgentActivatedAsync(IAgent agent) => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        _testRunId = Guid.NewGuid().ToString("N")[..8];
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        await OnClusterReadyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.DisposeAsync();
    }

    private string UniqueId(string prefix) => $"{prefix}-{_testRunId}";

    // ===== Behavior 1: Metadata =====

    [Fact]
    public async Task Behavior_Metadata_ReturnsIdAndCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("meta"));

        var metadata = await agent.GetMetadataAsync(ct);

        Assert.Equal(UniqueId("meta"), metadata.Id);
        Assert.Contains("state", metadata.Capabilities);
        Assert.Contains("history", metadata.Capabilities);
        Assert.Contains("events", metadata.Capabilities);
        Assert.Contains("notifications", metadata.Capabilities);
        Assert.Contains("tracking", metadata.Capabilities);
        Assert.Contains("streams", metadata.Capabilities);
        Assert.Contains("tools", metadata.Capabilities);
    }

    // ===== Behavior 2: State =====

    [Fact]
    public async Task Behavior_State_SetAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("state"));

        await agent.SetStateAsync("city", "Seattle", ct);
        var value = await agent.GetStateValueAsync("city", ct);

        Assert.Equal("Seattle", value);
    }

    [Fact]
    public async Task Behavior_State_IncrementPersists()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("state-inc"));

        var v1 = await agent.IncrementAsync("visits", ct);
        var v2 = await agent.IncrementAsync("visits", ct);
        var state = await agent.GetStateAsync(ct);

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
        Assert.Equal("2", state["visits"]);
    }

    [Fact]
    public async Task Behavior_State_GetAllReturnsSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("state-all"));

        await agent.SetStateAsync("a", "1", ct);
        await agent.SetStateAsync("b", "2", ct);
        var state = await agent.GetStateAsync(ct);

        Assert.Equal("1", state["a"]);
        Assert.Equal("2", state["b"]);
    }

    // ===== Behavior 3: History =====

    [Fact]
    public async Task Behavior_History_AddAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("hist"));

        await agent.AddHistoryAsync("user", "hello", ct);
        await agent.AddHistoryAsync("assistant", "hi there", ct);
        var history = await agent.GetHistoryAsync(ct);

        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("hello", history[0].Content);
        Assert.Equal("assistant", history[1].Role);
    }

    [Fact]
    public async Task Behavior_History_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("hist-stream");
        var agent = Agent(agentId);
        var stream = StreamProvider.GetStream<AgentHistoryEntry>(StreamId.Create("agent-history", agentId));
        var received = new TaskCompletionSource<AgentHistoryEntry>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.AddHistoryAsync("user", "stream-test", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("user", payload.Role);
        Assert.Equal("stream-test", payload.Content);
        await handle.UnsubscribeAsync();
    }

    // ===== Behavior 4: Events =====

    [Fact]
    public async Task Behavior_Events_PublishAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("events"));

        await agent.PublishEventAsync("test.event", "payload-1", ct);
        await agent.PublishEventAsync("test.event2", "payload-2", ct);
        var events = await agent.GetEventsAsync(ct);

        Assert.Equal(2, events.Count);
        Assert.Equal("test.event", events[0].Name);
        Assert.Equal("test.event2", events[1].Name);
    }

    [Fact]
    public async Task Behavior_Events_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("events-stream");
        var agent = Agent(agentId);
        var stream = StreamProvider.GetStream<AgentEventRecord>(StreamId.Create("agent-events", agentId));
        var received = new TaskCompletionSource<AgentEventRecord>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.PublishEventAsync("stream.test", "data", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("stream.test", payload.Name);
        Assert.Equal("data", payload.Payload);
        await handle.UnsubscribeAsync();
    }

    // ===== Behavior 5: Notifications =====

    [Fact]
    public async Task Behavior_Notifications_DeliveredToSubscriber()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("pub");
        var subscriberId = UniqueId("sub");

        var publisher = Agent(publisherId);
        var subscriber = Agent(subscriberId);

        await publisher.SubscribeAsync("test.topic", subscriberId, ct);
        await publisher.NotifyAsync("test.topic", "test-payload", ct);
        var notifications = await subscriber.GetNotificationsAsync(ct);

        Assert.Single(notifications);
        Assert.Equal("test.topic", notifications[0].Topic);
        Assert.Equal("test-payload", notifications[0].Payload);
    }

    [Fact]
    public async Task Behavior_Notifications_EnvelopeMetadataPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("pub-env");
        var subscriberId = UniqueId("sub-env");
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        var publisher = Agent(publisherId);
        var subscriber = Agent(subscriberId);

        await publisher.SubscribeAsync("test.topic", subscriberId, ct);
        await publisher.NotifyAsync(new NotificationEnvelope
        {
            Topic = "test.topic",
            Payload = "{\"key\":\"value\"}",
            ContentType = "application/json",
            Schema = "test.schema",
            SchemaVersion = "1.0",
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, string> { ["source"] = "testing" }
        }, ct);

        var notifications = await subscriber.GetNotificationsAsync(ct);
        var entry = Assert.Single(notifications);
        Assert.Equal("test.topic", entry.Topic);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("test.schema", entry.Schema);
        Assert.Equal("1.0", entry.SchemaVersion);
        Assert.Equal(messageId, entry.MessageId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("testing", entry.Headers["source"]);
    }

    [Fact]
    public async Task Behavior_Notifications_JsonPayloadRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("pub-json");
        var subscriberId = UniqueId("sub-json");

        var publisher = Agent(publisherId);
        var subscriber = Agent(subscriberId);

        await publisher.SubscribeAsync("test.topic", subscriberId, ct);
        await publisher.NotifyAsync(
            NotificationJson.CreateEnvelope("test.topic", new TestPayload("Seattle", 21)),
            ct);

        var notifications = await subscriber.GetNotificationsAsync(ct);
        var entry = Assert.Single(notifications);
        var typed = entry.ReadPayload<TestPayload>();

        Assert.NotNull(typed);
        Assert.Equal("Seattle", typed!.City);
        Assert.Equal(21, typed.Value);
    }

    [Fact]
    public async Task Behavior_Notifications_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("pub-nstream");
        var subscriberId = UniqueId("sub-nstream");

        var publisher = Agent(publisherId);
        var stream = StreamProvider.GetStream<NotificationRecord>(
            StreamId.Create("agent-notifications", subscriberId));
        var received = new TaskCompletionSource<NotificationRecord>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await publisher.SubscribeAsync("test.topic", subscriberId, ct);
        await publisher.NotifyAsync("test.topic", "stream-payload", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("test.topic", payload.Topic);
        Assert.Equal("stream-payload", payload.Payload);
        await handle.UnsubscribeAsync();
    }

    // ===== Behavior 6: Tracking =====

    [Fact]
    public async Task Behavior_Tracking_StartsAndStopsAtMaxTicks()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tracking"));

        await agent.StartTrackingAsync(TimeSpan.FromMilliseconds(40), 3, ct);
        var status = await WaitHelpers.WaitForTrackingToStopAsync(agent, ct);

        Assert.False(status.IsTracking);
        Assert.Equal(3, status.TickCount);
    }

    [Fact]
    public async Task Behavior_Tracking_ReminderIntervalStartsWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tracking-reminder"));

        await agent.StartTrackingAsync(TimeSpan.FromMinutes(1), 2, ct);
        var status = await agent.GetTrackingStatusAsync(ct);

        Assert.True(status.IsTracking);
        Assert.Equal(TimeSpan.FromMinutes(1), status.Interval);
        Assert.Equal(2, status.MaxTicks);

        await agent.StopTrackingAsync(ct);
    }

    // ===== Behavior 7: Tools =====

    [Fact]
    public async Task Behavior_Tools_MissingToolThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tools"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.InvokeToolAsync("nonexistent-tool", ct: ct));
    }

    // ===== Behavior 8: Streams =====

    [Fact]
    public async Task Behavior_Streams_PublishAndSubscribeRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("streams"));
        var streamGuid = Guid.NewGuid();
        var stream = StreamProvider.GetStream<string>(StreamId.Create("agent-tests", streamGuid));
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((payload, _) =>
        {
            received.TrySetResult(payload);
            return Task.CompletedTask;
        });

        await agent.PublishStreamAsync("agent-tests", streamGuid, "hello-stream", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("hello-stream", payload);
        await handle.UnsubscribeAsync();
    }

    // ===== Shared test contract =====

    private sealed record TestPayload(string City, int Value);
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add src/IAW.Testing/AgentTest.cs
git commit -m "feat: add AgentTest<T> base class with 8 universal behavior test suites"
```

---

### Task 8: Create AspireAgentTest<T> Base Class

**Files:**
- Create: `src/IAW.Testing/AspireAgentTest.cs`

**Step 1: Create the Aspire base class**

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core;
using IAW.Testing.Scenario;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IAW.Testing;

public abstract class AspireAgentTest<T> : IAsyncLifetime where T : class, IAgent
{
    private DistributedApplication _app = null!;
    private IHost _orleansClientHost = null!;
    private IClusterClient _orleansClient = null!;

    protected DistributedApplication App => _app;
    protected IClusterClient OrleansClient => _orleansClient;
    protected HttpClient HttpClient { get; private set; } = null!;

    protected ScenarioBuilder Scenario => new(Agent);

    protected IAgent Agent(string id) => _orleansClient.GetGrain<IAgent>(id);

    protected virtual string[] AppHostArgs => ["--Parameters:anthropic-api-key=test-key"];
    protected virtual string WaitForResource => "samples";
    protected virtual string OrleansSiloResource => "samples";
    protected virtual string OrleansSiloEndpointName => "orleans-gateway";
    protected virtual TimeSpan StartupTimeout => TimeSpan.FromMinutes(3);

    protected virtual Task OnAppStartedAsync() => Task.CompletedTask;
    protected virtual Task OnBeforeTestAsync() => Task.CompletedTask;
    protected virtual Task OnAfterTestAsync() => Task.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>(AppHostArgs);
        _app = await appHost.BuildAsync();

        using var startTimeout = new CancellationTokenSource(StartupTimeout);
        await _app.StartAsync(startTimeout.Token);
        await _app.ResourceNotifications
            .WaitForResourceAsync(WaitForResource, KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60), startTimeout.Token);

        HttpClient = _app.CreateHttpClient(OrleansSiloResource);
        var gatewayEndpoint = _app.GetEndpoint(OrleansSiloResource, OrleansSiloEndpointName);

        _orleansClientHost = Host.CreateApplicationBuilder()
            .UseOrleansClient(client =>
            {
                client.UseLocalhostClustering(
                    gatewayPort: gatewayEndpoint.Port,
                    serviceId: "default",
                    clusterId: "default");
                client.AddMemoryStreams("agents");
            })
            .Build();

        await _orleansClientHost.StartAsync(startTimeout.Token);
        _orleansClient = _orleansClientHost.Services.GetRequiredService<IClusterClient>();

        await OnAppStartedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();
        await _orleansClientHost.StopAsync();
        _orleansClientHost.Dispose();
        await _app.DisposeAsync();
    }
}
```

**Step 2: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded. Note: this file references `Projects.Aspire` which requires adding a project reference to the AppHost.

**Step 3: Add AppHost project reference to IAW.Testing.csproj**

Add this to the `<ItemGroup>` with project references:

```xml
<ProjectReference Include="..\IAW.AppHost\Aspire.csproj" />
```

**Step 4: Verify it builds**

Run: `dotnet build src/IAW.Testing/IAW.Testing.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add src/IAW.Testing/AspireAgentTest.cs src/IAW.Testing/IAW.Testing.csproj
git commit -m "feat: add AspireAgentTest<T> base class for cross-silo testing"
```

---

### Task 9: Create test/Core.Tests Project

**Files:**
- Create: `test/Core.Tests/IAW.Core.Tests.csproj`
- Create: `test/Core.Tests/CoreAgentTests.cs`
- Move: `test/Agents.Tests/ArchitectureGuardTests.cs` → `test/Core.Tests/ArchitectureGuardTests.cs`

**Step 1: Create the test project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\IAW.Testing\IAW.Testing.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

**Step 2: Create CoreAgentTests.cs**

This one class inherits all 8 behavior test suites — approximately 20 test methods — with zero custom code:

```csharp
using Core;
using IAW.Testing;

namespace IAW.Core.Tests;

public sealed class CoreAgentTests : AgentTest<Agent>;
```

**Step 3: Copy ArchitectureGuardTests.cs**

Copy `test/Agents.Tests/ArchitectureGuardTests.cs` to `test/Core.Tests/ArchitectureGuardTests.cs` and update the namespace:

```csharp
using Core;
using System.Reflection;
using Xunit;

namespace IAW.Core.Tests;

public sealed class ArchitectureGuardTests
{
    [Fact]
    public void CoreAgent_DoesNotExposeLegacyChannelStreamingMethods()
    {
        var coreAssembly = typeof(IAgent).Assembly;
        var legacyAgentType = coreAssembly.GetType("Core.Agent", throwOnError: true, ignoreCase: false);
        Assert.NotNull(legacyAgentType);

        var methods = legacyAgentType!.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(methods, method =>
            method.Name == "PublishStreamAsync" &&
            method.GetParameters() is
            [
                { ParameterType: not null } p0,
                { ParameterType: not null } p1,
                { ParameterType: not null } p2
            ] &&
            p0.ParameterType == typeof(string) &&
            p1.ParameterType == typeof(string) &&
            p2.ParameterType == typeof(CancellationToken));

        Assert.DoesNotContain(methods, method =>
            method.Name == "SubscribeStreamAsync" &&
            method.GetParameters() is
            [
                { ParameterType: not null } p0,
                { ParameterType: not null } p1
            ] &&
            p0.ParameterType == typeof(string) &&
            p1.ParameterType == typeof(CancellationToken));

        Assert.DoesNotContain(methods, method =>
            method.Name == "GetStreamSubscriberCountsAsync");
    }

    [Fact]
    public void CoreAssembly_DoesNotContainLegacyChannelStreamingTypes()
    {
        var coreAssembly = typeof(IAgent).Assembly;

        Assert.Null(coreAssembly.GetType("Core.AgentStreamHub", throwOnError: false, ignoreCase: false));
        Assert.Null(coreAssembly.GetType("Core.AgentTopicChannel", throwOnError: false, ignoreCase: false));
        Assert.Null(coreAssembly.GetType("Core.AgentStreamSubscription", throwOnError: false, ignoreCase: false));
    }

    [Fact]
    public void CoreAssembly_AgentIsPublicAndExtendsDurableGrain()
    {
        var coreAssembly = typeof(IAgent).Assembly;
        var agentType = coreAssembly.GetType("Core.Agent", throwOnError: true, ignoreCase: false);

        Assert.NotNull(agentType);
        Assert.True(agentType!.IsPublic);
        Assert.True(typeof(IAgent).IsPublic);
        Assert.True(typeof(Orleans.Journaling.DurableGrain).IsAssignableFrom(agentType));

        var weatherAgentType = coreAssembly.GetType("Core.WeatherAgent", throwOnError: false, ignoreCase: false);
        Assert.Null(weatherAgentType);
    }
}
```

**Step 4: Add to solution**

Run: `cd E:/IAW/InteractiveAgents/IAW && dotnet sln IAW.slnx add test/Core.Tests/IAW.Core.Tests.csproj`

**Step 5: Verify it builds**

Run: `dotnet build test/Core.Tests/IAW.Core.Tests.csproj`
Expected: Build succeeded.

**Step 6: Run tests to verify all 8 behavior suites pass**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --verbosity normal`
Expected: ~20 tests pass (8 behavior suites × multiple facts each + 3 architecture guards).

**Step 7: Commit**

```bash
git add test/Core.Tests/ IAW.slnx
git commit -m "feat: add Core.Tests with AgentTest<Agent> — all behaviors tested automatically"
```

---

### Task 10: Create Scenario Builder Tests

**Files:**
- Create: `test/Core.Tests/ScenarioBuilderTests.cs`

**Step 1: Write scenario builder tests**

```csharp
using Core;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public sealed class ScenarioBuilderTests : AgentTest<Agent>
{
    [Fact]
    public async Task Scenario_NotificationDelivery()
    {
        await Scenario
            .Given(Scenario.Agent("pub-s1")).Subscribes("alert", to: "sub-s1")
            .When(Scenario.Agent("pub-s1")).Notifies("alert", "fire")
            .Then(Scenario.Agent("sub-s1")).HasNotification("alert", "fire")
            .RunAsync();
    }

    [Fact]
    public async Task Scenario_StateManipulation()
    {
        await Scenario
            .When(Scenario.Agent("counter-s1")).SetsState("city", "Seattle")
            .When(Scenario.Agent("counter-s1")).Increments("visits")
            .When(Scenario.Agent("counter-s1")).Increments("visits")
            .Then(Scenario.Agent("counter-s1")).HasState("city", "Seattle")
            .Then(Scenario.Agent("counter-s1")).HasState("visits", "2")
            .RunAsync();
    }

    [Fact]
    public async Task Scenario_MultiAgentNotification()
    {
        await Scenario
            .Given(Scenario.Agent("hub-s1")).Subscribes("alert", to: "a-s1")
            .Given(Scenario.Agent("hub-s1")).Subscribes("alert", to: "b-s1")
            .When(Scenario.Agent("hub-s1")).Notifies("alert", "fire")
            .Then(Scenario.Agent("a-s1")).HasNotification("alert", "fire")
            .Then(Scenario.Agent("b-s1")).HasNotification("alert", "fire")
            .RunAsync();
    }

    [Fact]
    public async Task Scenario_EventPublishing()
    {
        await Scenario
            .When(Scenario.Agent("evt-s1")).PublishesEvent("test.event", "data")
            .Then(Scenario.Agent("evt-s1")).HasEvent("test.event")
            .RunAsync();
    }

    [Fact]
    public async Task Scenario_EnvelopeNotification()
    {
        await Scenario
            .Given(Scenario.Agent("pub-env-s1")).Subscribes("weather", to: "sub-env-s1")
            .When(Scenario.Agent("pub-env-s1")).NotifiesWithEnvelope(new NotificationEnvelope
            {
                Topic = "weather",
                Payload = "{\"city\":\"Seattle\"}",
                Schema = "weather",
                SchemaVersion = "1.0"
            })
            .Then(Scenario.Agent("sub-env-s1")).HasNotificationMatching(n =>
                n.Schema == "weather" && n.SchemaVersion == "1.0")
            .RunAsync();
    }

    [Fact]
    public async Task Scenario_HistoryTracking()
    {
        await Scenario
            .When(Scenario.Agent("hist-s1")).AddsHistory("user", "hello")
            .When(Scenario.Agent("hist-s1")).AddsHistory("assistant", "hi")
            .Then(Scenario.Agent("hist-s1")).HasHistory(2)
            .RunAsync();
    }
}
```

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --verbosity normal`
Expected: All tests pass including scenario builder tests.

**Step 3: Commit**

```bash
git add test/Core.Tests/ScenarioBuilderTests.cs
git commit -m "feat: add scenario builder tests for multi-agent orchestration"
```

---

### Task 11: Refactor Integration.Tests to Use AspireAgentTest<T>

**Files:**
- Modify: `test/Integration.Tests/IAW.Integration.Tests.csproj`
- Modify: `test/Integration.Tests/OrleansAgentIntegrationTests.cs`

**Step 1: Update Integration.Tests csproj**

Add IAW.Testing project reference and remove redundant Orleans packages (they come transitively from IAW.Testing):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\IAW.Testing\IAW.Testing.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

Note: The `Core.csproj` and `Aspire.csproj` project references are no longer needed directly because `IAW.Testing.csproj` references both.

**Step 2: Refactor the test class to inherit from AspireAgentTest<Agent>**

Replace the boilerplate `InitializeAsync`/`DisposeAsync` with the base class. The test class becomes:

```csharp
using Core;
using IAW.Testing;
using System.Net;
using System.Text.Json;
using Xunit;

namespace IAW.Integration.Tests;

public sealed class OrleansAgentIntegrationTests : AspireAgentTest<Agent>
{
    // All setup/teardown is handled by AspireAgentTest<Agent>

    [Fact]
    public void AspireTestingHost_ExposesOrleansTestResource()
    {
        var endpoint = App.GetEndpoint("samples", "orleans-gateway");
        Assert.False(string.IsNullOrWhiteSpace(endpoint.Host));
        Assert.True(endpoint.Port > 0);
    }

    // Keep all existing [Fact] test methods, replacing:
    //   _samplesClient     → HttpClient
    //   _orleansClient     → OrleansClient
    //   _app               → App
    // The rest of the test logic stays the same.

    [Fact]
    public async Task OrleansSampleEndpoints_ReportExpectedBehavior()
    {
        var ct = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid().ToString("N");

        var state = await GetJsonAsync($"/samples/orleans-agent/state?agentId=integration-{runId}&city=Seattle", ct);
        Assert.True(state.GetProperty("isStateful").GetBoolean());

        var legacyState = await GetJsonAsync("/samples/agent/state", ct);
        Assert.True(legacyState.GetProperty("count").GetInt32() >= 3);
        Assert.Equal("Seattle", legacyState.GetProperty("city").GetString());

        var metadata = await GetJsonAsync("/samples/orleans-agent/metadata", ct);
        var capabilities = metadata
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        Assert.Contains("state", capabilities);
        Assert.Contains("streams", capabilities);
    }

    [Fact]
    public async Task OrleansStateEndpoint_SameAgentId_PersistsVisitCounterAcrossRequests()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = $"integration-state-persist-{Guid.NewGuid():N}";

        var first = await GetJsonAsync($"/samples/orleans-agent/state?agentId={agentId}&city=Seattle", ct);
        var second = await GetJsonAsync($"/samples/orleans-agent/state?agentId={agentId}&city=Seattle", ct);

        Assert.True(first.GetProperty("isStateful").GetBoolean());
        Assert.Equal(1, first.GetProperty("visit1").GetInt32());
        Assert.Equal(2, first.GetProperty("visit2").GetInt32());

        Assert.True(second.GetProperty("isStateful").GetBoolean());
        Assert.Equal(3, second.GetProperty("visit1").GetInt32());
        Assert.Equal(4, second.GetProperty("visit2").GetInt32());

        var stateFromClient = await OrleansClient.GetGrain<IAgent>(agentId).GetStateAsync(ct);
        Assert.Equal("Seattle", stateFromClient["city"]);
        Assert.Equal("4", stateFromClient["visits"]);
    }

    [Fact]
    public async Task Scenario_CrossSilo_NotificationDelivery()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = $"integration-scenario-pub-{Guid.NewGuid():N}";
        var subscriberId = $"integration-scenario-sub-{Guid.NewGuid():N}";

        await Scenario
            .Given(Scenario.Agent(publisherId)).Subscribes("weather.alert", to: subscriberId)
            .When(Scenario.Agent(publisherId)).Notifies("weather.alert", "storm")
            .Then(Scenario.Agent(subscriberId)).HasNotification("weather.alert", "storm")
            .RunAsync(ct);
    }

    private async Task<JsonElement> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await HttpClient.GetAsync(path, ct);
        var payloadText = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"GET {path} failed with {(int)response.StatusCode} ({response.StatusCode}). Response: {payloadText}");

        using var document = JsonDocument.Parse(payloadText);
        return document.RootElement.Clone();
    }
}
```

Note: The full refactoring should preserve all existing test methods, just replacing field references. The implementer should carefully map every `_samplesClient` → `HttpClient`, `_oleansClient` → `OrleansClient`, `_app` → `App`, and remove the old `InitializeAsync`/`DisposeAsync`.

**Step 3: Verify it builds**

Run: `dotnet build test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: Build succeeded.

**Step 4: Run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --verbosity normal`
Expected: All tests pass (including the new Scenario-based cross-silo test).

**Step 5: Commit**

```bash
git add test/Integration.Tests/
git commit -m "refactor: Integration.Tests inherits AspireAgentTest<Agent>"
```

---

### Task 12: Delete Agents.Tests and Update Solution

**Files:**
- Delete: `test/Agents.Tests/` (entire directory)
- Modify: `IAW.slnx`

**Step 1: Remove from solution**

Run: `cd E:/IAW/InteractiveAgents/IAW && dotnet sln IAW.slnx remove test/Agents.Tests/IAW.Agents.Tests.csproj`

**Step 2: Delete the directory**

Run: `rm -rf test/Agents.Tests/`

**Step 3: Verify solution builds**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

**Step 4: Run all tests**

Run: `dotnet test IAW.slnx --verbosity normal`
Expected: All tests pass — Core.Tests (behavior suites + architecture guards + scenario tests), Integration.Tests (Aspire cross-silo), TelegramBot.Tests (model tests).

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove Agents.Tests — replaced by Core.Tests with AgentTest<Agent>"
```

---

### Task 13: Final Verification — Full Build + All Tests

**Step 1: Clean build**

Run: `dotnet build IAW.slnx --no-incremental`
Expected: Build succeeded with 0 errors.

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx --verbosity normal`
Expected: All tests pass.

**Step 3: Verify solution structure**

The final `IAW.slnx` should contain:

```xml
<Solution>
  <Project Path="samples/Samples/Samples.csproj" />
  <Project Path="src/Core/Core.csproj" />
  <Project Path="src/DevUI/DevUI.csproj" />
  <Project Path="src/IAW.AppHost/Aspire.csproj" />
  <Project Path="src/IAW.MCP/MCP.csproj" />
  <Project Path="src/IAW.Testing/IAW.Testing.csproj" />
  <Project Path="src/Clients.Telegram.Bot/TelegramBot.csproj" />
  <Project Path="src/IAW.ServiceDefaults/ServiceDefaults.csproj" />
  <Project Path="test/Core.Tests/IAW.Core.Tests.csproj" />
  <Project Path="test/Integration.Tests/IAW.Integration.Tests.csproj" />
  <Project Path="test/TelegramBot.Tests/TelegramBot.Tests.csproj" />
</Solution>
```
