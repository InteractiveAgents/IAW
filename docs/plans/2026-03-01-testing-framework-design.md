# IAW Testing Framework Design

## Summary

A testing framework for IAW agents shipped as the `IAW.Testing` NuGet package. Provides two base classes — `AgentTest<T>` (in-process Orleans TestCluster) and `AspireAgentTest<T>` (Aspire AppHost cross-silo) — with a shared fluent `Scenario` builder for orchestrating multi-agent interactions.

## Goals

- Every `IAgent` implementation automatically passes 8 universal behavior test suites by inheriting `AgentTest<T>`
- Fluent `Scenario` builder for readable multi-agent test orchestration (Given/When/Then)
- `MockChatClient` for LLM simulation without real API calls
- Lifecycle hooks for subclass customization
- Aspire-based cross-silo testing via `AspireAgentTest<T>`
- Ships as NuGet so consumers of IAW can test their own agents

## Package Structure

```
src/IAW.Testing/
  AgentTest.cs              -- Base class: TestCluster + 8 behavior test suites
  AspireAgentTest.cs        -- Base class: Aspire AppHost + Orleans client
  Scenario/
    ScenarioBuilder.cs      -- Fluent Given/When/Then builder
    GivenStep.cs            -- Given-phase step definitions
    WhenStep.cs             -- When-phase step definitions
    ThenStep.cs             -- Then-phase assertions
    AgentRef.cs             -- Agent reference wrapper for fluent API
    StreamRef.cs            -- Stream reference wrapper for fluent API
  MockChatClient.cs         -- IChatClient mock for LLM simulation
  AgentTestConfigurator.cs  -- Default silo configurator (memory storage/streams/reminders)
  WaitHelpers.cs            -- Polling/timeout utilities
  IAW.Testing.csproj
```

### Dependencies

- `Core` (project reference)
- `Orleans.TestingHost`
- `Aspire.Hosting.Testing`
- `xunit.v3`

## Test Project Reorganization

| Current | Action |
|---------|--------|
| `test/Agents.Tests/` | Delete — replaced by `AgentTest<Agent>` |
| `test/Integration.Tests/` | Refactor to use `AspireAgentTest<Agent>` as base |
| `test/TelegramBot.Tests/` | Keep as-is (domain model tests) |
| `test/Core.Tests/` (new) | `class CoreAgentTests : AgentTest<Agent> {}` — all 8 suites auto-pass |

## AgentTest<T> Base Class

```csharp
public abstract class AgentTest<T> : IAsyncLifetime where T : class, IAgent
{
    protected TestCluster Cluster { get; }
    protected IStreamProvider StreamProvider { get; }
    protected ScenarioBuilder Scenario { get; }
    protected MockChatClient MockLlm { get; }

    protected IAgent Agent(string id);

    // Silo customization
    protected virtual void ConfigureSilo(ISiloBuilder builder) { }

    // Lifecycle hooks
    protected virtual Task OnClusterReadyAsync() => Task.CompletedTask;
    protected virtual Task OnBeforeTestAsync() => Task.CompletedTask;
    protected virtual Task OnAfterTestAsync() => Task.CompletedTask;
    protected virtual Task OnAgentActivatedAsync(IAgent agent) => Task.CompletedTask;
}
```

### 8 Universal Behavior Test Suites (Auto-Generated)

| Behavior | Tests |
|----------|-------|
| Metadata | Returns ID, capabilities list includes all 7 defaults |
| State | Set/get roundtrip, increment persists, get-all snapshot |
| History | Add/get roundtrip, ordering preserved, stream emitted |
| Events | Publish/get roundtrip, ordering preserved, stream emitted |
| Notifications | Subscribe/notify delivery, envelope metadata, JSON payload roundtrip, stream emitted |
| Tracking | Start/stop, max-tick enforcement, reminder-based interval |
| Tools | Invoke returns result (if DefineTools overridden), missing tool throws |
| Streams | Publish/subscribe roundtrip via custom namespace |

Consumer usage:

```csharp
public class MyCustomAgentTests : AgentTest<MyCustomAgent>
{
    // All 8 behavior suites pass automatically

    [Fact]
    public async Task MyAgent_CustomTool_Works()
    {
        var agent = Agent("test-1");
        var result = await agent.InvokeToolAsync("my-tool", new() { ["param"] = "value" });
        Assert.Equal("expected", result);
    }
}
```

## Scenario Builder Fluent API

```csharp
await Scenario
    .Given(Agent("publisher")).Subscribes("weather.alert", to: "processor")
    .When(Agent("publisher")).Notifies("weather.alert", "storm")
    .Then(Agent("processor")).HasNotification("weather.alert", "storm")
    .RunAsync();
```

### Available Step Verbs

| Phase | Verb | Purpose |
|-------|------|---------|
| Given | `Subscribes(topic, to)` | Register agent subscription |
| Given | `HasState(key, value)` | Pre-set agent state |
| Given | `HasHistory(role, content)` | Pre-add history entry |
| When | `Notifies(topic, payload)` | Send simple notification |
| When | `NotifiesWithEnvelope(envelope)` | Send envelope notification |
| When | `PublishesEvent(name, payload)` | Publish event |
| When | `PublishesStream(ns, id, msg)` | Publish to custom stream |
| When | `SetsState(key, value)` | Set state |
| When | `Increments(key)` | Increment counter |
| When | `AddsHistory(role, content)` | Add history entry |
| When | `SendsLlm(message)` | Send message through LLM (uses mock) |
| Then | `HasNotification(topic, payload)` | Assert notification received |
| Then | `HasNotificationMatching(predicate)` | Assert notification matching predicate |
| Then | `HasEvent(name)` | Assert event published |
| Then | `HasState(key, value)` | Assert state value |
| Then | `HasHistory(count)` | Assert history count |
| Then | `HasTrackingStatus(predicate)` | Assert tracking status |

`RunAsync()` executes steps sequentially with automatic polling/wait for async operations (configurable timeout, default 5s).

### Multi-Agent Scenario Examples

```csharp
// Two agents subscribe to same topic, both receive
await Scenario
    .Given(Agent("hub")).Subscribes("alert", to: "agent-a")
    .Given(Agent("hub")).Subscribes("alert", to: "agent-b")
    .When(Agent("hub")).Notifies("alert", "fire")
    .Then(Agent("agent-a")).HasNotification("alert", "fire")
    .Then(Agent("agent-b")).HasNotification("alert", "fire")
    .RunAsync();

// Stream publish/subscribe
await Scenario
    .Given(Stream("my-namespace", streamId)).HasSubscriber(Agent("listener"))
    .When(Agent("sender")).PublishesStream("my-namespace", streamId, "hello")
    .Then(Stream("my-namespace", streamId)).Received("hello")
    .RunAsync();

// State manipulation sequence
await Scenario
    .When(Agent("counter")).SetsState("city", "Seattle")
    .When(Agent("counter")).Increments("visits")
    .When(Agent("counter")).Increments("visits")
    .Then(Agent("counter")).HasState("city", "Seattle")
    .Then(Agent("counter")).HasState("visits", "2")
    .RunAsync();

// Envelope notification with typed payload
await Scenario
    .Given(Agent("pub")).Subscribes("weather.alert", to: "sub")
    .When(Agent("pub")).NotifiesWithEnvelope(new NotificationEnvelope
    {
        Topic = "weather.alert",
        Payload = "{\"city\":\"Seattle\"}",
        Schema = "weather.alert",
        SchemaVersion = "1.0"
    })
    .Then(Agent("sub")).HasNotificationMatching(n =>
        n.Schema == "weather.alert" && n.SchemaVersion == "1.0")
    .RunAsync();
```

## AspireAgentTest<T>

```csharp
public abstract class AspireAgentTest<T> : IAsyncLifetime where T : class, IAgent
{
    protected DistributedApplication App { get; }
    protected IClusterClient OrleansClient { get; }
    protected HttpClient HttpClient { get; }
    protected ScenarioBuilder Scenario { get; }

    protected IAgent Agent(string id);

    // AppHost configuration
    protected virtual string[] AppHostArgs => ["--Parameters:anthropic-api-key=test-key"];
    protected virtual string WaitForResource => "samples";

    // Lifecycle hooks
    protected virtual Task OnAppStartedAsync() => Task.CompletedTask;
    protected virtual Task OnBeforeTestAsync() => Task.CompletedTask;
    protected virtual Task OnAfterTestAsync() => Task.CompletedTask;
}
```

Same `Scenario` builder API — agents accessed via network Orleans client instead of in-process TestCluster.

## MockChatClient

```csharp
public class MockChatClient : IChatClient
{
    public MockChatClient ReturnsText(string response);
    public MockChatClient ReturnsStream(params string[] chunks);
    public MockChatClient ThrowsOnSend(Exception ex);

    public int SendCount { get; }
    public List<string> ReceivedMessages { get; }
}
```

Usage in tests:

```csharp
public class MyAgentTests : AgentTest<MyCustomAgent>
{
    [Fact]
    public async Task Agent_StreamsLlmResponse()
    {
        MockLlm.ReturnsStream("Hello", " world");

        await Scenario
            .When(Agent("test")).SendsLlm("Hi there")
            .Then(Agent("test")).HasHistory(2)
            .RunAsync();
    }
}
```

## Architecture Guard Tests

The existing `ArchitectureGuardTests.cs` (reflection-based design validation) moves to `test/Core.Tests/ArchitectureGuardTests.cs` unchanged.
