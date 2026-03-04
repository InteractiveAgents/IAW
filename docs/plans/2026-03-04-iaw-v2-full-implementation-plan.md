# IAW V2 Complete Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete redesign of IAW from V1 to V2 — new Agent base class, Telegram bot as full assistant hub, MCP server with full orchestration API, comprehensive docs site, and open-source polish.

**Architecture:** Orleans 10.0 DurableGrain-based agents with a single flat `AgentV2` base class (no optional interfaces). Runtime-injected state via `[Memory]` attributes hidden from derived agents. Clean break from V1 — no adapters, no `[Obsolete]`.

**Tech Stack:** .NET 11, Orleans 10.0, Aspire 13.x, Microsoft.Extensions.AI, VitePress, Telegram.BotAPI, ModelContextProtocol, Qdrant, xunit.v3

---

## Phase 1: AgentV2 Core (Steps 1–60)

### Task 1: V2 Contract Additions

**Files:**
- Create: `src/Core/V2/AgentEvent.cs`
- Create: `src/Core/V2/AgentEventQuery.cs`
- Create: `src/Core/V2/ScheduleStatus.cs`

**Step 1:** Create `AgentEvent.cs` with `[GenerateSerializer]`:
```csharp
[GenerateSerializer]
public sealed record AgentEvent(
    [property: Id(0)] string EventId,
    [property: Id(1)] string Type,
    [property: Id(2)] string Payload,
    [property: Id(3)] DateTime TimestampUtc,
    [property: Id(4)] Dictionary<string, string>? Metadata = null);
```

**Step 2:** Create `AgentEventQuery.cs`:
```csharp
[GenerateSerializer]
public sealed record AgentEventQuery(
    [property: Id(0)] int? Limit = null,
    [property: Id(1)] DateTime? SinceUtc = null,
    [property: Id(2)] string? Type = null,
    [property: Id(3)] bool Descending = false);
```

**Step 3:** Create `ScheduleStatus.cs`:
```csharp
[GenerateSerializer]
public sealed record ScheduleStatus(
    [property: Id(0)] bool IsRunning,
    [property: Id(1)] TimeSpan Interval,
    [property: Id(2)] int TickCount,
    [property: Id(3)] int? MaxTicks);
```

**Step 4:** Build to verify contracts compile:
Run: `dotnet build src/Core/Core.csproj`
Expected: SUCCESS

**Step 5:** Commit
```bash
git add src/Core/V2/AgentEvent.cs src/Core/V2/AgentEventQuery.cs src/Core/V2/ScheduleStatus.cs
git commit -m "feat(v2): add AgentEvent, AgentEventQuery, ScheduleStatus contracts"
```

---

### Task 2: Expand IAgentV2 with Full API

**Files:**
- Modify: `src/Core/V2/IAgentV2.cs`

**Step 6:** Add event, notification, scheduling, streaming, and tool methods to `IAgentV2`:
```csharp
public interface IAgentV2 : IGrainWithStringKey
{
    // Profile
    Task<AgentProfile> GetProfileAsync(CancellationToken ct = default);

    // Conversation
    Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default);
    Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query = null, CancellationToken ct = default);

    // Memory
    Task SetMemoryAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken ct = default);

    // Events
    Task AppendEventAsync(AgentEvent evt, CancellationToken ct = default);
    Task<List<AgentEvent>> QueryEventsAsync(AgentEventQuery? query = null, CancellationToken ct = default);

    // Notifications
    Task SubscribeAsync(string agentId, CancellationToken ct = default);
    Task PublishNotificationAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task ReceiveNotificationAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task<List<NotificationRecord>> QueryNotificationsAsync(CancellationToken ct = default);

    // Scheduling
    Task StartScheduleAsync(TimeSpan interval, int? maxTicks = null, CancellationToken ct = default);
    Task StopScheduleAsync(CancellationToken ct = default);
    Task<ScheduleStatus> GetScheduleStatusAsync(CancellationToken ct = default);

    // Streaming
    Task PublishStreamAsync<T>(string ns, string streamId, T message, CancellationToken ct = default);
}
```

**Step 7:** Build to verify:
Run: `dotnet build src/Core/Core.csproj`
Expected: SUCCESS

**Step 8:** Commit
```bash
git add src/Core/V2/IAgentV2.cs
git commit -m "feat(v2): expand IAgentV2 with events, notifications, scheduling, streaming"
```

---

### Task 3: AgentV2 Base Class — Skeleton

**Files:**
- Create: `src/Core/V2/AgentV2.cs`

**Step 9:** Create the `AgentV2` base class with state fields and abstract members:
```csharp
public abstract class AgentV2 : DurableGrain, IAgentV2, IRemindable
{
    [Memory("messages")] private readonly IDurableList<AgentMessage> _messages = null!;
    [Memory("memory")] private readonly IDurableDictionary<string, string> _memory = null!;
    [Memory("events")] private readonly IDurableList<AgentEvent> _events = null!;
    [Memory("subscriptions")] private readonly IDurableDictionary<string, string> _subscriptions = null!;
    [Memory("notifications")] private readonly IDurableList<NotificationRecord> _notifications = null!;

    protected IDurableList<AgentMessage> Messages => _messages;
    protected IDurableDictionary<string, string> Memory => _memory;
    protected IDurableList<AgentEvent> Events => _events;

    protected abstract AgentProfile Profile { get; }

    protected virtual Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct)
        => Task.FromResult(new AgentReply("Not implemented"));

    protected virtual IEnumerable<AITool> DefineTools() => [];
}
```

**Step 10:** Build to verify skeleton compiles:
Run: `dotnet build src/Core/Core.csproj`
Expected: SUCCESS

**Step 11:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): add AgentV2 base class skeleton"
```

---

### Task 4: AgentV2 — Profile Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 12:** Implement `GetProfileAsync`:
```csharp
public Task<AgentProfile> GetProfileAsync(CancellationToken ct)
    => Task.FromResult(Profile);
```

**Step 13:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 14:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement GetProfileAsync"
```

---

### Task 5: AgentV2 — Memory Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 15:** Implement `SetMemoryAsync` and `GetMemoryAsync`:
```csharp
public async Task SetMemoryAsync(string key, string value, CancellationToken ct)
{
    _memory[key] = value;
    await WriteStateAsync();
}

public Task<string?> GetMemoryAsync(string key, CancellationToken ct)
    => Task.FromResult(_memory.TryGetValue(key, out var value) ? value : null);
```

**Step 16:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 17:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement memory set/get"
```

---

### Task 6: AgentV2 — Message History Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 18:** Implement `AppendMessageAsync` and `QueryMessagesAsync`:
```csharp
public async Task AppendMessageAsync(AgentMessage message, CancellationToken ct)
{
    _messages.Add(message);
    await WriteStateAsync();
    await PublishBehaviorStreamAsync("agent-history", message, ct);
}

public Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query, CancellationToken ct)
{
    IEnumerable<AgentMessage> result = _messages;
    if (query is not null)
    {
        if (query.Role is not null)
            result = result.Where(m => m.Role == query.Role);
        if (query.SinceUtc is not null)
            result = result.Where(m => m.TimestampUtc >= query.SinceUtc);
        if (query.Descending)
            result = result.OrderByDescending(m => m.TimestampUtc);
        if (query.Limit is not null)
            result = result.Take(query.Limit.Value);
    }
    return Task.FromResult(result.ToList());
}
```

**Step 19:** Add the `PublishBehaviorStreamAsync` helper method (port from V1 Agent):
```csharp
private async Task PublishBehaviorStreamAsync<T>(string ns, T message, CancellationToken ct)
{
    var provider = this.GetStreamProvider("agents");
    var stream = provider.GetStream<T>(StreamId.Create(ns, this.GetPrimaryKeyString()));
    await stream.OnNextAsync(message);
}
```

**Step 20:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 21:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement message history with streaming"
```

---

### Task 7: AgentV2 — Events Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 22:** Implement `AppendEventAsync` and `QueryEventsAsync`:
```csharp
public async Task AppendEventAsync(AgentEvent evt, CancellationToken ct)
{
    _events.Add(evt);
    await WriteStateAsync();
    await PublishBehaviorStreamAsync("agent-events", evt, ct);
}

public Task<List<AgentEvent>> QueryEventsAsync(AgentEventQuery? query, CancellationToken ct)
{
    IEnumerable<AgentEvent> result = _events;
    if (query is not null)
    {
        if (query.Type is not null)
            result = result.Where(e => e.Type == query.Type);
        if (query.SinceUtc is not null)
            result = result.Where(e => e.TimestampUtc >= query.SinceUtc);
        if (query.Descending)
            result = result.OrderByDescending(e => e.TimestampUtc);
        if (query.Limit is not null)
            result = result.Take(query.Limit.Value);
    }
    return Task.FromResult(result.ToList());
}
```

**Step 23:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 24:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement events with streaming"
```

---

### Task 8: AgentV2 — Notifications Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 25:** Implement notification methods:
```csharp
public async Task SubscribeAsync(string agentId, CancellationToken ct)
{
    _subscriptions[agentId] = agentId;
    await WriteStateAsync();
}

public async Task PublishNotificationAsync(NotificationEnvelope envelope, CancellationToken ct)
{
    foreach (var subscriberId in _subscriptions.Keys)
    {
        var subscriber = GrainFactory.GetGrain<IAgentV2>(subscriberId);
        await subscriber.ReceiveNotificationAsync(envelope, ct);
    }
}

public async Task ReceiveNotificationAsync(NotificationEnvelope envelope, CancellationToken ct)
{
    var record = new NotificationRecord(
        envelope.Source, envelope.Target, envelope.ContentType,
        envelope.Payload, envelope.Schema, envelope.SchemaVersion,
        envelope.MessageId, envelope.CorrelationId, envelope.Headers);
    _notifications.Add(record);
    await WriteStateAsync();
    await PublishBehaviorStreamAsync("agent-notifications", record, ct);
}

public Task<List<NotificationRecord>> QueryNotificationsAsync(CancellationToken ct)
    => Task.FromResult(_notifications.ToList());
```

**Step 26:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 27:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement notifications with pub/sub"
```

---

### Task 9: AgentV2 — Scheduling Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 28:** Add scheduling state and implement methods:
```csharp
// Fields for scheduling (tracked in memory dict)
private const string ScheduleIntervalKey = "__schedule_interval";
private const string ScheduleMaxTicksKey = "__schedule_max_ticks";
private const string ScheduleTickCountKey = "__schedule_tick_count";

public async Task StartScheduleAsync(TimeSpan interval, int? maxTicks, CancellationToken ct)
{
    _memory[ScheduleIntervalKey] = interval.TotalMilliseconds.ToString();
    if (maxTicks.HasValue) _memory[ScheduleMaxTicksKey] = maxTicks.Value.ToString();
    _memory[ScheduleTickCountKey] = "0";
    await WriteStateAsync();
    await this.RegisterOrUpdateReminder("schedule", interval, interval);
}

public async Task StopScheduleAsync(CancellationToken ct)
{
    var reminder = await this.GetReminder("schedule");
    if (reminder is not null) await this.UnregisterReminder(reminder);
    _memory.Remove(ScheduleIntervalKey);
    _memory.Remove(ScheduleMaxTicksKey);
    _memory.Remove(ScheduleTickCountKey);
    await WriteStateAsync();
}

public Task<ScheduleStatus> GetScheduleStatusAsync(CancellationToken ct)
{
    var isRunning = _memory.ContainsKey(ScheduleIntervalKey);
    var interval = isRunning ? TimeSpan.FromMilliseconds(double.Parse(_memory[ScheduleIntervalKey])) : TimeSpan.Zero;
    var tickCount = _memory.TryGetValue(ScheduleTickCountKey, out var tc) ? int.Parse(tc) : 0;
    var maxTicks = _memory.TryGetValue(ScheduleMaxTicksKey, out var mt) ? int.Parse(mt) : (int?)null;
    return Task.FromResult(new ScheduleStatus(isRunning, interval, tickCount, maxTicks));
}

public async Task ReceiveReminder(string reminderName, TickStatus status)
{
    if (reminderName != "schedule") return;
    var tickCount = _memory.TryGetValue(ScheduleTickCountKey, out var tc) ? int.Parse(tc) : 0;
    tickCount++;
    _memory[ScheduleTickCountKey] = tickCount.ToString();
    await WriteStateAsync();

    await OnScheduleTickAsync(tickCount, CancellationToken.None);

    if (_memory.TryGetValue(ScheduleMaxTicksKey, out var mt) && tickCount >= int.Parse(mt))
        await StopScheduleAsync(CancellationToken.None);
}

protected virtual Task OnScheduleTickAsync(int tickCount, CancellationToken ct) => Task.CompletedTask;
```

**Step 29:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 30:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement scheduling via reminders"
```

---

### Task 10: AgentV2 — Streaming Implementation

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 31:** Implement `PublishStreamAsync`:
```csharp
public async Task PublishStreamAsync<T>(string ns, string streamId, T message, CancellationToken ct)
{
    var provider = this.GetStreamProvider("agents");
    var stream = provider.GetStream<T>(StreamId.Create(ns, streamId));
    await stream.OnNextAsync(message);
}
```

**Step 32:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 33:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement stream publishing"
```

---

### Task 11: AgentV2 — RespondAsync with LLM Integration

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 34:** Implement `RespondAsync` that delegates to `OnRespondAsync`, manages history, and integrates observability:
```csharp
private static readonly ActivitySource ActivitySource = new("IAW.AgentV2");

public async Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct)
{
    using var activity = ActivitySource.StartActivity("AgentV2.Respond");
    activity?.SetTag("agent.id", this.GetPrimaryKeyString());
    activity?.SetTag("agent.name", Profile.DisplayName);

    var userMessage = new AgentMessage(
        Guid.NewGuid().ToString(), "user", request.Input, DateTime.UtcNow, request.Metadata);
    _messages.Add(userMessage);
    await WriteStateAsync();

    try
    {
        var reply = await OnRespondAsync(request, ct);

        var assistantMessage = new AgentMessage(
            Guid.NewGuid().ToString(), "assistant", reply.Output, DateTime.UtcNow, reply.Metadata);
        _messages.Add(assistantMessage);
        await WriteStateAsync();

        await PublishBehaviorStreamAsync("agent-history", assistantMessage, ct);
        return reply;
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        throw;
    }
}
```

**Step 35:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 36:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): implement RespondAsync with history and observability"
```

---

### Task 12: AgentV2 Test Infrastructure — AgentTestV2<T>

**Files:**
- Create: `src/IAW.Testing/AgentTestV2.cs`

**Step 37:** Create new test base class for V2 agents:
```csharp
public abstract class AgentTestV2<T> where T : AgentV2
{
    // Orleans TestCluster setup matching existing AgentTest<T> pattern
    // but targeting IAgentV2 interface
}
```

Port the test cluster setup from existing `AgentTest<T>` but target `IAgentV2` grain references.

**Step 38:** Add behavior tests for Profile:
```csharp
[Fact]
public async Task GetProfile_ReturnsNonNull()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    var profile = await agent.GetProfileAsync();
    Assert.NotNull(profile);
    Assert.NotEmpty(profile.Id);
}
```

**Step 39:** Add behavior tests for Memory:
```csharp
[Fact]
public async Task Memory_SetAndGet_RoundTrips()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    await agent.SetMemoryAsync("key1", "value1");
    var result = await agent.GetMemoryAsync("key1");
    Assert.Equal("value1", result);
}

[Fact]
public async Task Memory_GetMissing_ReturnsNull()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    var result = await agent.GetMemoryAsync("nonexistent");
    Assert.Null(result);
}
```

**Step 40:** Add behavior tests for Messages:
```csharp
[Fact]
public async Task Messages_AppendAndQuery_RoundTrips()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    var msg = new AgentMessage(Guid.NewGuid().ToString(), "user", "hello", DateTime.UtcNow);
    await agent.AppendMessageAsync(msg);
    var messages = await agent.QueryMessagesAsync();
    Assert.Contains(messages, m => m.Content == "hello");
}
```

**Step 41:** Add behavior tests for Events:
```csharp
[Fact]
public async Task Events_AppendAndQuery_RoundTrips()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    var evt = new AgentEvent(Guid.NewGuid().ToString(), "test.event", "{}", DateTime.UtcNow);
    await agent.AppendEventAsync(evt);
    var events = await agent.QueryEventsAsync();
    Assert.Contains(events, e => e.Type == "test.event");
}
```

**Step 42:** Add behavior tests for Notifications:
```csharp
[Fact]
public async Task Notifications_SubscribeAndPublish_Delivers()
{
    var publisher = Cluster.GrainFactory.GetGrain<IAgentV2>("publisher");
    var subscriber = Cluster.GrainFactory.GetGrain<IAgentV2>("subscriber");
    await publisher.SubscribeAsync("subscriber");
    var envelope = new NotificationEnvelope("publisher", "subscriber", "text/plain", "hello", null, null, Guid.NewGuid().ToString(), null, null);
    await publisher.PublishNotificationAsync(envelope);
    var notifications = await subscriber.QueryNotificationsAsync();
    Assert.Single(notifications);
}
```

**Step 43:** Add behavior tests for Scheduling:
```csharp
[Fact]
public async Task Schedule_StartAndGetStatus_ReportsRunning()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    await agent.StartScheduleAsync(TimeSpan.FromMinutes(1), maxTicks: 5);
    var status = await agent.GetScheduleStatusAsync();
    Assert.True(status.IsRunning);
    Assert.Equal(5, status.MaxTicks);
}
```

**Step 44:** Add behavior tests for Streaming:
```csharp
[Fact]
public async Task Stream_PublishAndSubscribe_Delivers()
{
    var agent = Cluster.GrainFactory.GetGrain<IAgentV2>("test-agent");
    var received = new TaskCompletionSource<string>();
    var provider = Cluster.Client.GetStreamProvider("agents");
    var stream = provider.GetStream<string>(StreamId.Create("test-ns", "test-stream"));
    await stream.SubscribeAsync((msg, _) => { received.SetResult(msg); return Task.CompletedTask; });
    await agent.PublishStreamAsync("test-ns", "test-stream", "payload");
    var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("payload", result);
}
```

**Step 45:** Run all new V2 tests:
Run: `dotnet test src/IAW.Testing/IAW.Testing.csproj`
Expected: All pass (assuming test agent is registered)

**Step 46:** Commit
```bash
git add src/IAW.Testing/AgentTestV2.cs
git commit -m "feat(v2): add AgentTestV2<T> with universal behavior tests"
```

---

### Task 13: Create Concrete Test Agent for V2

**Files:**
- Create: `test/Core.Tests/TestAgentV2.cs`
- Create: `test/Core.Tests/CoreAgentV2Tests.cs`

**Step 47:** Create a minimal test agent:
```csharp
public sealed class TestAgentV2 : AgentV2
{
    protected override AgentProfile Profile => new("test-agent", "Test Agent V2", "A test agent", []);
    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct)
        => Task.FromResult(new AgentReply($"Echo: {request.Input}"));
}
```

**Step 48:** Create test class inheriting AgentTestV2:
```csharp
public sealed class CoreAgentV2Tests : AgentTestV2<TestAgentV2>;
```

**Step 49:** Run V2 tests:
Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~CoreAgentV2"`
Expected: All behavior tests pass

**Step 50:** Commit
```bash
git add test/Core.Tests/TestAgentV2.cs test/Core.Tests/CoreAgentV2Tests.cs
git commit -m "test(v2): add CoreAgentV2Tests with TestAgentV2 agent"
```

---

### Task 14: Verify V1 Tests Still Green

**Step 51:** Run all existing V1 tests to confirm no regressions:
Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All 41 tests pass

**Step 52:** Run full solution build:
Run: `dotnet build IAW.slnx`
Expected: SUCCESS

**Step 53:** Commit (if any fixes needed)

---

### Task 15: AgentV2 — RespondAsync with IChatClient Tools Integration

**Files:**
- Modify: `src/Core/V2/AgentV2.cs`

**Step 54:** Add `OnActivateAsync` to configure tools and chat client:
```csharp
private IChatClient? _chatClient;

protected IChatClient? ChatClient => _chatClient;

public override Task OnActivateAsync(CancellationToken ct)
{
    // Derived agents inject IChatClient via constructor with [Llm<TModel>]
    // AgentV2 picks it up via service resolution if available
    return base.OnActivateAsync(ct);
}

protected void ActivateChatClient(IChatClient client)
{
    _chatClient = client;
}
```

**Step 55:** Enhance `OnRespondAsync` default to use chat client when available:
```csharp
protected virtual async Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct)
{
    if (_chatClient is null)
        return new AgentReply("No chat client configured");

    var chatMessages = new List<ChatMessage>();
    if (!string.IsNullOrEmpty(Profile.Instructions))
        chatMessages.Add(new ChatMessage(ChatRole.System, Profile.Instructions));

    foreach (var msg in _messages)
        chatMessages.Add(new ChatMessage(new ChatRole(msg.Role), msg.Content));

    chatMessages.Add(new ChatMessage(ChatRole.User, request.Input));

    var options = new ChatOptions { Tools = DefineTools().ToList() };
    var response = await _chatClient.GetResponseAsync(chatMessages, options, ct);
    var output = string.Join("", response.Messages.Where(m => m.Role == ChatRole.Assistant).Select(m => m.Text));
    return new AgentReply(output, response.ModelId);
}
```

**Step 56:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 57:** Commit
```bash
git add src/Core/V2/AgentV2.cs
git commit -m "feat(v2): integrate IChatClient and tools into RespondAsync"
```

---

### Task 16: Architecture Guard Tests for V2

**Files:**
- Modify: `test/Core.Tests/ArchitectureGuardTests.cs`

**Step 58:** Add V2 architecture guard tests:
```csharp
[Fact]
public void AgentV2_ExtendsOnlyDurableGrain()
{
    Assert.Equal(typeof(DurableGrain), typeof(AgentV2).BaseType);
}

[Fact]
public void AgentV2_ImplementsIAgentV2()
{
    Assert.True(typeof(IAgentV2).IsAssignableFrom(typeof(AgentV2)));
}

[Fact]
public void IAgentV2_HasNoMemoryParametersInDerivedConstructors()
{
    // Verify TestAgentV2 constructor has no [Memory] params
    var ctor = typeof(TestAgentV2).GetConstructors().First();
    var memoryParams = ctor.GetParameters()
        .Where(p => p.GetCustomAttributes(typeof(MemoryAttribute), true).Any());
    Assert.Empty(memoryParams);
}
```

**Step 59:** Run:
Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~Architecture"`
Expected: All pass

**Step 60:** Commit
```bash
git add test/Core.Tests/ArchitectureGuardTests.cs
git commit -m "test(v2): add architecture guard tests for AgentV2"
```

---

## Phase 2: V1 Removal (Steps 61–90)

### Task 17: Remove V1 IAgentBehaviors

**Files:**
- Delete: `src/Core/IAgentBehaviors.cs`
- Modify: `src/Core/IAgent.cs`

**Step 61:** Read `IAgentBehaviors.cs` to understand all 8 behavior interfaces that need removal

**Step 62:** Delete `IAgentBehaviors.cs`

**Step 63:** Update `IAgent.cs` to simply alias `IAgentV2`:
```csharp
// Transitional — will be removed entirely
[Obsolete("Use IAgentV2")]
public interface IAgent : IAgentV2;
```

Or if clean break: delete `IAgent.cs` entirely and update all references to `IAgentV2`.

**Step 64:** Build to find all compilation errors:
Run: `dotnet build IAW.slnx`
Expected: Many errors — catalog them all

**Step 65:** Commit removal only (not fixes yet):
```bash
git add -u
git commit -m "refactor(v2): remove V1 IAgentBehaviors"
```

---

### Task 18: Migrate V1 Agent Base Class to AgentV2

**Files:**
- Modify: `src/Core/Agent.cs`

**Step 66:** Read current `Agent.cs` (600 lines) thoroughly

**Step 67:** Replace V1 `Agent` class body — make it extend `AgentV2` as a compatibility shim or delete entirely depending on compilation errors from Step 64

**Step 68:** If keeping as shim:
```csharp
[Obsolete("Extend AgentV2 directly")]
public abstract class Agent : AgentV2
{
    // Redirect old API to new
    public virtual string DisplayName => Profile.DisplayName;
    public virtual string SystemPrompt => Profile.Instructions ?? "";

    public Task<AgentMetadata> GetMetadataAsync()
        => Task.FromResult(new AgentMetadata(this.GetPrimaryKeyString(), DisplayName, Profile.Capabilities));

    // Other V1 methods redirect to V2 equivalents...
}
```

**Step 69:** Build:
Run: `dotnet build src/Core/Core.csproj`

**Step 70:** Commit
```bash
git add src/Core/Agent.cs
git commit -m "refactor(v2): convert Agent to AgentV2 compatibility shim"
```

---

### Task 19: Fix Compilation Errors — Samples

**Files:**
- Modify: `samples/Samples/Program.cs`
- Modify: `samples/Samples/GitHubTestAgent.cs` → rename to `GitHubTest.cs`

**Step 71:** Rename `GitHubTestAgent` to `GitHubTest` and migrate to AgentV2:
```csharp
public sealed class GitHubTest([Llm<GitHubGpt4oMini>] IChatClient llm) : AgentV2
{
    protected override AgentProfile Profile => new("github-test", "GitHub Test", "Test agent using GitHub Models", []);
    protected override async Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct)
    {
        ActivateChatClient(llm);
        return await base.OnRespondAsync(request, ct);
    }
}
```

**Step 72:** Update `Program.cs` sample endpoints to use `IAgentV2` grain references and V2 methods

**Step 73:** Build samples:
Run: `dotnet build samples/Samples/Samples.csproj`

**Step 74:** Commit
```bash
git add samples/
git commit -m "refactor(v2): migrate samples to AgentV2"
```

---

### Task 20: Fix Compilation Errors — Telegram Bot

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversationGrain.cs` → becomes `TelegramConversation.cs`
- Modify: `src/Clients.Telegram.Bot/ITelegramConversation.cs`
- Modify: `src/Clients.Telegram.Bot/AgentRouterGrain.cs` → becomes `AgentRouter.cs`
- Modify: `src/Clients.Telegram.Bot/MonitorSourceProviderGrain.cs` → becomes `MonitorSourceProvider.cs`
- Modify: `src/Clients.Telegram.Bot/Program.cs`

**Step 75:** Update `ITelegramConversation` to extend `IAgentV2` instead of `IAgent`

**Step 76:** Rename and migrate `TelegramConversationGrain` → `TelegramConversation` extending `AgentV2`

**Step 77:** Rename `AgentRouterGrain` → `AgentRouter`, update to use `IAgentV2` grain refs

**Step 78:** Rename `MonitorSourceProviderGrain` → `MonitorSourceProvider`

**Step 79:** Update `Program.cs` webhook handler to use V2 types

**Step 80:** Build:
Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`

**Step 81:** Commit
```bash
git add src/Clients.Telegram.Bot/
git commit -m "refactor(v2): migrate Telegram bot to AgentV2, rename to drop Grain suffix"
```

---

### Task 21: Fix Compilation Errors — MCP Server

**Files:**
- Modify: `src/IAW.MCP/Tools/AgentTools.cs`

**Step 82:** Update `AgentTools` to use `IAgentV2` grain references

**Step 83:** Update `agent_list_all` to call `GetProfileAsync` instead of `GetMetadataAsync`

**Step 84:** Update `assistant_chat` to call `RespondAsync` instead of old API

**Step 85:** Build:
Run: `dotnet build src/IAW.MCP/MCP.csproj`

**Step 86:** Commit
```bash
git add src/IAW.MCP/
git commit -m "refactor(v2): migrate MCP tools to IAgentV2"
```

---

### Task 22: Fix Compilation Errors — Testing Framework

**Files:**
- Modify: `src/IAW.Testing/AgentTest.cs`
- Modify: `src/IAW.Testing/AspireAgentTest.cs`
- Modify: `src/IAW.Testing/ScenarioBuilder.cs`

**Step 87:** Update `AgentTest<T>` to target `IAgentV2` or rewrite to `AgentTestV2<T>`

**Step 88:** Update `AspireAgentTest<T>` for V2

**Step 89:** Update `ScenarioBuilder` for V2 API

**Step 90:** Commit
```bash
git add src/IAW.Testing/
git commit -m "refactor(v2): migrate testing framework to V2"
```

---

### Task 23: Fix Compilation Errors — DevUI

**Files:**
- Modify: `src/DevUI/Program.cs`

**Step 91:** Update DevUI agents to V2 patterns

**Step 92:** Build:
Run: `dotnet build src/DevUI/DevUI.csproj`

**Step 93:** Commit
```bash
git add src/DevUI/
git commit -m "refactor(v2): migrate DevUI to V2"
```

---

### Task 24: Delete V1 Artifacts

**Files:**
- Delete: `src/Core/IAgent.cs` (if still exists as shim)
- Delete: `src/Core/AgentContracts.cs` (V1 contracts — only if all V1 types moved to V2)
- Clean up any remaining V1 references

**Step 94:** Search for any remaining references to V1 types:
Run: `grep -r "IAgent[^V]" src/ --include="*.cs"` (find non-V2 IAgent refs)

**Step 95:** Delete or update remaining V1 files

**Step 96:** Full solution build:
Run: `dotnet build IAW.slnx`
Expected: SUCCESS with zero warnings about V1

**Step 97:** Commit
```bash
git add -u
git commit -m "refactor(v2): remove all V1 artifacts"
```

---

### Task 25: Full Test Suite Green

**Step 98:** Run all unit tests:
Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All pass

**Step 99:** Run integration tests:
Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: All pass

**Step 100:** Run with Aspire:
Run: `aspire run`
Expected: All resources healthy

**Step 101:** Commit any test fixes
```bash
git commit -m "fix(v2): ensure all tests green after V1 removal"
```

---

## Phase 3: Telegram Bot — Full Assistant Hub (Steps 102–160)

### Task 26: TelegramConversation — V2 Core

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversation.cs`

**Step 102:** Read current TelegramConversation implementation

**Step 103:** Refactor constructor to V2 pattern — only domain dependencies:
```csharp
public sealed class TelegramConversation(
    [Llm<Claude45Haiku>] IChatClient llm,
    ITelegramBotClient botClient) : AgentV2
{
    protected override AgentProfile Profile => new(
        this.GetPrimaryKeyString(),
        "Telegram Conversation",
        "Handles Telegram chat conversations with agent routing",
        ["chat", "telegram", "voice", "monitors"]);
}
```

**Step 104:** Migrate `SendAsync` logic to `OnRespondAsync`

**Step 105:** Build + test:
Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`

**Step 106:** Commit
```bash
git commit -m "refactor(v2): migrate TelegramConversation to AgentV2"
```

---

### Task 27: AgentRouter — V2 Migration

**Files:**
- Modify: `src/Clients.Telegram.Bot/AgentRouter.cs`

**Step 107:** Read current AgentRouter (Qdrant-based vector routing)

**Step 108:** Migrate to AgentV2, update grain references to IAgentV2

**Step 109:** Update routing to call `RespondAsync` on routed agents

**Step 110:** Build + test

**Step 111:** Commit
```bash
git commit -m "refactor(v2): migrate AgentRouter to V2"
```

---

### Task 28: Voice Pipeline — Finish Whisper Integration

**Files:**
- Modify: `src/Clients.Telegram.Bot/VoiceTranscriptionService.cs`

**Step 112:** Research Whisper .NET integration options (Foundry Local or whisper.net)

**Step 113:** Implement transcription service:
```csharp
public async Task<string> TranscribeAsync(Stream audioWav, CancellationToken ct)
{
    // Use configured Whisper endpoint or local model
    // Return transcribed text
}
```

**Step 114:** Wire voice pipeline: OGG → AudioConverter → WAV → VoiceTranscriptionService → text → RespondAsync

**Step 115:** Test voice message flow end-to-end

**Step 116:** Commit
```bash
git commit -m "feat: implement voice transcription pipeline"
```

---

### Task 29: Monitor Source Provider — RSS/Feed Polling

**Files:**
- Modify: `src/Clients.Telegram.Bot/MonitorSourceProvider.cs`
- Modify: `src/Core/IMonitorSourceProvider.cs`

**Step 117:** Read current MonitorSourceProvider implementation

**Step 118:** Migrate to V2, implement RSS feed polling with scheduling:
```csharp
public sealed class MonitorSourceProvider : AgentV2
{
    protected override async Task OnScheduleTickAsync(int tickCount, CancellationToken ct)
    {
        // Poll configured RSS feeds
        // Publish notifications for new items
    }
}
```

**Step 119:** Implement RSS feed parsing (System.ServiceModel.Syndication)

**Step 120:** Implement notification delivery to subscribed conversations

**Step 121:** Build + test

**Step 122:** Commit
```bash
git commit -m "feat: implement RSS monitor polling with V2 scheduling"
```

---

### Task 30: Telegram — Inline Keyboard Interactions

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversation.cs`

**Step 123:** Implement callback query handling for inline keyboard buttons

**Step 124:** Add command routing (/start, /help, /subscribe, /monitors)

**Step 125:** Build + test

**Step 126:** Commit
```bash
git commit -m "feat: implement Telegram inline keyboard and command routing"
```

---

### Task 31: Telegram — Topic Threading in Groups

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversation.cs`

**Step 127:** Read existing topic support code

**Step 128:** Ensure topic-based message routing works with V2

**Step 129:** Test group chat with topic threads

**Step 130:** Commit
```bash
git commit -m "feat: verify topic threading in group chats"
```

---

### Task 32: Telegram — Error Handling and Graceful Degradation

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramConversation.cs`
- Modify: `src/Clients.Telegram.Bot/Program.cs`

**Step 131:** Add try/catch with user-friendly error messages in webhook handler

**Step 132:** Handle LLM timeout gracefully (typing indicator, retry)

**Step 133:** Handle agent routing failure (fallback to personal assistant)

**Step 134:** Handle voice transcription failure (text reply explaining issue)

**Step 135:** Build + test

**Step 136:** Commit
```bash
git commit -m "feat: add error handling and graceful degradation to Telegram bot"
```

---

### Task 33: Telegram — Webhook Setup Robustness

**Files:**
- Modify: `src/Clients.Telegram.Bot/WebhookSetupService.cs`

**Step 137:** Read current webhook setup service

**Step 138:** Add retry logic for ngrok tunnel discovery

**Step 139:** Add health check endpoint for webhook status

**Step 140:** Build + test

**Step 141:** Commit
```bash
git commit -m "feat: improve webhook setup robustness"
```

---

### Task 34: Telegram Integration Tests

**Files:**
- Create: `test/Integration.Tests/TelegramBotIntegrationTests.cs`

**Step 142:** Create integration test class:
```csharp
public sealed class TelegramBotIntegrationTests : AspireAgentTest<TelegramConversation>
{
    [Fact]
    public async Task TelegramConversation_RespondAsync_ReturnsReply() { ... }

    [Fact]
    public async Task AgentRouter_RoutesToCorrectAgent() { ... }
}
```

**Step 143–148:** Write and run tests for:
- Basic conversation flow
- Agent routing
- Monitor subscription
- Error handling
- Voice pipeline (mocked)
- Topic threading

**Step 149:** Run all integration tests:
Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`

**Step 150:** Commit
```bash
git commit -m "test: add Telegram bot integration tests"
```

---

### Task 35: Telegram — End-to-End Verification

**Step 151:** Run Aspire:
Run: `aspire run`

**Step 152:** Verify Telegram bot starts and registers webhook

**Step 153:** Test /start command in Telegram

**Step 154:** Test text conversation flow

**Step 155:** Test agent routing (ask different types of questions)

**Step 156:** Test voice message (if Whisper available)

**Step 157:** Test monitor subscription

**Step 158:** Test error scenarios (invalid input, LLM down)

**Step 159:** Fix any issues found

**Step 160:** Commit
```bash
git commit -m "fix: address Telegram E2E verification findings"
```

---

## Phase 4: MCP Server — Full Orchestration API (Steps 161–200)

### Task 36: MCP — agent_send_message Tool

**Files:**
- Modify: `src/IAW.MCP/Tools/AgentTools.cs`

**Step 161:** Implement `agent_send_message`:
```csharp
[McpServerTool, Description("Send a message to any agent by ID")]
public static async Task<string> AgentSendMessage(
    IClusterClient client,
    [Description("The agent grain ID")] string agentId,
    [Description("The message to send")] string message)
{
    var agent = client.GetGrain<IAgentV2>(agentId);
    var request = new AgentRequest(message);
    var reply = await agent.RespondAsync(request);
    return reply.Output;
}
```

**Step 162:** Build + test

**Step 163:** Commit
```bash
git commit -m "feat(mcp): implement agent_send_message tool"
```

---

### Task 37: MCP — agent_get_status Tool

**Step 164:** Implement `agent_get_status`:
```csharp
[McpServerTool, Description("Get agent profile and recent activity")]
public static async Task<string> AgentGetStatus(
    IClusterClient client,
    [Description("The agent grain ID")] string agentId)
{
    var agent = client.GetGrain<IAgentV2>(agentId);
    var profile = await agent.GetProfileAsync();
    var messages = await agent.QueryMessagesAsync(new AgentMessageQuery(Limit: 5, Descending: true));
    return JsonSerializer.Serialize(new { profile, recentMessages = messages });
}
```

**Step 165:** Build + test

**Step 166:** Commit
```bash
git commit -m "feat(mcp): implement agent_get_status tool"
```

---

### Task 38: MCP — agent_assign_task Tool

**Step 167:** Implement `agent_assign_task`:
```csharp
[McpServerTool, Description("Assign a task to PersonalAssistant for delegation")]
public static async Task<string> AgentAssignTask(
    IClusterClient client,
    [Description("Task description")] string task,
    [Description("Priority: low, medium, high")] string priority = "medium")
{
    var pa = client.GetGrain<IAgentV2>("personal-assistant");
    var request = new AgentRequest(task, Metadata: new() { ["priority"] = priority, ["type"] = "task" });
    var reply = await pa.RespondAsync(request);
    return reply.Output;
}
```

**Step 168:** Build + test

**Step 169:** Commit
```bash
git commit -m "feat(mcp): implement agent_assign_task tool"
```

---

### Task 39: MCP — agent_get_events Tool

**Step 170:** Implement `agent_get_events`:
```csharp
[McpServerTool, Description("Get events from an agent")]
public static async Task<string> AgentGetEvents(
    IClusterClient client,
    [Description("The agent grain ID")] string agentId,
    [Description("Max events to return")] int limit = 20)
{
    var agent = client.GetGrain<IAgentV2>(agentId);
    var events = await agent.QueryEventsAsync(new AgentEventQuery(Limit: limit, Descending: true));
    return JsonSerializer.Serialize(events);
}
```

**Step 171:** Build + test

**Step 172:** Commit
```bash
git commit -m "feat(mcp): implement agent_get_events tool"
```

---

### Task 40: MCP — agent_get_metrics Tool

**Step 173:** Implement `agent_get_metrics`:
```csharp
[McpServerTool, Description("Get agent performance metrics")]
public static async Task<string> AgentGetMetrics(
    IClusterClient client,
    [Description("The agent grain ID")] string agentId)
{
    var agent = client.GetGrain<IAgentV2>(agentId);
    var profile = await agent.GetProfileAsync();
    var messageCount = (await agent.QueryMessagesAsync()).Count;
    var eventCount = (await agent.QueryEventsAsync()).Count;
    var schedule = await agent.GetScheduleStatusAsync();
    return JsonSerializer.Serialize(new { profile.Id, profile.DisplayName, messageCount, eventCount, schedule });
}
```

**Step 174:** Build + test

**Step 175:** Commit
```bash
git commit -m "feat(mcp): implement agent_get_metrics tool"
```

---

### Task 41: MCP — agent_trigger_self_improvement Tool

**Step 176:** Implement `agent_trigger_self_improvement`:
```csharp
[McpServerTool, Description("Trigger self-improvement analysis")]
public static async Task<string> AgentTriggerSelfImprovement(IClusterClient client)
{
    var agent = client.GetGrain<IAgentV2>("self-improvement");
    var request = new AgentRequest("Analyze recent agent interactions and propose improvements");
    var reply = await agent.RespondAsync(request);
    return reply.Output;
}
```

**Step 177:** Build + test

**Step 178:** Commit
```bash
git commit -m "feat(mcp): implement agent_trigger_self_improvement tool"
```

---

### Task 42: MCP — Update agent_list_all for V2

**Step 179:** Update existing `agent_list_all` to use `GetProfileAsync`:
```csharp
[McpServerTool, Description("List all registered agents")]
public static async Task<string> AgentListAll(IClusterClient client)
{
    var agentIds = new[] { "personal-assistant", "roslyn", "dotnet", /* ...all 15 */ };
    var profiles = new List<AgentProfile>();
    foreach (var id in agentIds)
    {
        var agent = client.GetGrain<IAgentV2>(id);
        profiles.Add(await agent.GetProfileAsync());
    }
    return JsonSerializer.Serialize(profiles);
}
```

**Step 180:** Build + test

**Step 181:** Commit
```bash
git commit -m "refactor(mcp): update agent_list_all for V2"
```

---

### Task 43: MCP — Update assistant_chat for V2

**Step 182:** Update `assistant_chat` to use `RespondAsync`

**Step 183:** Build + test

**Step 184:** Commit
```bash
git commit -m "refactor(mcp): update assistant_chat for V2"
```

---

### Task 44: MCP Integration Tests

**Files:**
- Create: `test/Integration.Tests/McpToolsIntegrationTests.cs`

**Step 185–190:** Write integration tests for each MCP tool

**Step 191:** Run all MCP tests:
Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter "FullyQualifiedName~Mcp"`

**Step 192:** Commit
```bash
git commit -m "test: add MCP tools integration tests"
```

---

### Task 45: MCP — End-to-End Verification

**Step 193:** Run Aspire:
Run: `aspire run`

**Step 194:** Test MCP tools via Claude Code or HTTP transport

**Step 195–198:** Verify each tool works end-to-end

**Step 199:** Fix any issues

**Step 200:** Commit
```bash
git commit -m "fix: address MCP E2E verification findings"
```

---

## Phase 5: Documentation (Steps 201–250)

### Task 46: README Complete Rewrite

**Files:**
- Modify: `README.md`

**Step 201:** Write new README with:
- Hero description of IAW
- Quick install (NuGet packages)
- Minimal "Hello Agent" code example using AgentV2
- Feature grid
- Architecture overview (1 paragraph)
- Links to full docs site
- Badge: CI, NuGet, Docs
- Contributing link

**Step 202:** Commit
```bash
git commit -m "docs: rewrite README for V2"
```

---

### Task 47: Getting Started Guide

**Files:**
- Create/Modify: `website/guide/getting-started.md`

**Step 203:** Write getting started guide:
- Prerequisites (.NET 11, Aspire workload)
- Install NuGet packages
- Create first AgentV2
- Register in AppHost
- Run with `aspire run`
- Test via HTTP endpoint

**Step 204:** Commit
```bash
git commit -m "docs: write getting-started guide"
```

---

### Task 48: Architecture Guide

**Files:**
- Create/Modify: `website/guide/architecture.md`

**Step 205:** Write architecture doc:
- Orleans grain model
- AgentV2 base class design
- State management (DurableGrain + Memory)
- LLM integration layer
- Aspire hosting model
- Multi-silo topology

**Step 206:** Commit
```bash
git commit -m "docs: write architecture guide"
```

---

### Task 49: AgentV2 API Reference

**Files:**
- Create/Modify: `website/guide/agent-v2.md`

**Step 207:** Document IAgentV2 interface — every method with examples

**Step 208:** Document AgentV2 base class — overridable members, protected accessors

**Step 209:** Document all V2 contracts (AgentProfile, AgentRequest, AgentReply, AgentMessage, AgentEvent, etc.)

**Step 210:** Commit
```bash
git commit -m "docs: write AgentV2 API reference"
```

---

### Task 50: Telegram Bot Guide

**Files:**
- Create/Modify: `website/guide/telegram-bot.md`

**Step 211:** Write Telegram setup guide:
- Bot creation via BotFather
- Configuration (BotToken, NgrokApiUrl)
- AppHost setup
- Webhook registration
- Agent routing explained
- Voice message handling
- Monitor subscriptions

**Step 212:** Commit
```bash
git commit -m "docs: write Telegram bot guide"
```

---

### Task 51: MCP Server Guide

**Files:**
- Create/Modify: `website/guide/mcp-server.md`

**Step 213:** Write MCP integration guide:
- What is MCP
- Available tools and their usage
- Claude Code configuration
- Example workflows

**Step 214:** Commit
```bash
git commit -m "docs: write MCP server guide"
```

---

### Task 52: Deployment Guide

**Files:**
- Create/Modify: `website/guide/deployment.md`

**Step 215:** Write deployment guide:
- Local development with Aspire
- Docker container deployment
- Azure Container Apps
- Environment variables reference
- LLM API key configuration
- Qdrant setup

**Step 216:** Commit
```bash
git commit -m "docs: write deployment guide"
```

---

### Task 53: Configuration Reference

**Files:**
- Create/Modify: `website/reference/configuration.md`

**Step 217:** Document all configuration:
- AppHost parameters and secrets
- LLM model configuration (AI:LLM:Models:*)
- Telegram config
- Orleans clustering config
- Qdrant config

**Step 218:** Commit
```bash
git commit -m "docs: write configuration reference"
```

---

### Task 54: IAgentV2 Interface Reference

**Files:**
- Create/Modify: `website/reference/iagent-v2.md`

**Step 219:** Full interface reference with method signatures, parameters, return types, and usage examples

**Step 220:** Commit
```bash
git commit -m "docs: write IAgentV2 interface reference"
```

---

### Task 55: V2 Contracts Reference

**Files:**
- Create/Modify: `website/reference/contracts.md`

**Step 221:** Document all serializable V2 contracts

**Step 222:** Commit
```bash
git commit -m "docs: write V2 contracts reference"
```

---

### Task 56: Samples Guide

**Files:**
- Create/Modify: `website/reference/samples.md`

**Step 223:** Document each sample project and what it demonstrates

**Step 224:** Commit
```bash
git commit -m "docs: write samples guide"
```

---

### Task 57: Testing Guide

**Files:**
- Create/Modify: `website/reference/testing.md`

**Step 225:** Document testing patterns:
- AgentTestV2<T> base class
- AspireAgentTest<T> for integration
- ScenarioBuilder for multi-agent tests
- MockChatClient for LLM simulation

**Step 226:** Commit
```bash
git commit -m "docs: write testing guide"
```

---

### Task 58: Tutorial — Build Your First Agent

**Files:**
- Create: `website/tutorials/build-your-first-agent.md`

**Step 227:** Step-by-step tutorial:
1. Create .NET project
2. Add IAW.Core NuGet
3. Create agent class extending AgentV2
4. Override Profile and OnRespondAsync
5. Register in AppHost
6. Run and test

**Step 228:** Commit
```bash
git commit -m "docs: write build-your-first-agent tutorial"
```

---

### Task 59: Tutorial — Multi-Agent Orchestration

**Files:**
- Create: `website/tutorials/multi-agent-orchestration.md`

**Step 229:** Tutorial covering:
- Creating multiple agents
- Agent-to-agent communication via notifications
- Routing messages between agents
- Orchestration patterns

**Step 230:** Commit
```bash
git commit -m "docs: write multi-agent orchestration tutorial"
```

---

### Task 60: Tutorial — Custom Tools

**Files:**
- Create: `website/tutorials/custom-tools.md`

**Step 231:** Tutorial covering:
- Defining tools via DefineTools()
- Tool execution in OnRespondAsync
- External service integration

**Step 232:** Commit
```bash
git commit -m "docs: write custom tools tutorial"
```

---

### Task 61: Tutorial — Telegram Integration

**Files:**
- Create: `website/tutorials/telegram-integration.md`

**Step 233:** Tutorial for building Telegram bot with IAW

**Step 234:** Commit
```bash
git commit -m "docs: write Telegram integration tutorial"
```

---

### Task 62: Website Landing Page

**Files:**
- Modify: `website/index.md`

**Step 235:** Create professional landing page:
- Hero with tagline
- Quick code example
- Feature grid (Orleans, LLM, Aspire, Multi-agent)
- Links to getting started
- GitHub link

**Step 236:** Commit
```bash
git commit -m "docs: create landing page"
```

---

### Task 63: Website Navigation and Config

**Files:**
- Modify: `website/.vitepress/config.ts` (or equivalent)

**Step 237:** Configure sidebar navigation matching all new pages

**Step 238:** Configure search, theme, social links

**Step 239:** Build website:
Run: `cd website && npm run build`
Expected: SUCCESS

**Step 240:** Commit
```bash
git commit -m "docs: configure website navigation"
```

---

### Task 64: CHANGELOG Update

**Files:**
- Modify: `CHANGELOG.md`

**Step 241:** Write V2 changelog entry documenting all breaking changes and new features

**Step 242:** Commit
```bash
git commit -m "docs: update CHANGELOG for V2"
```

---

### Task 65: CONTRIBUTING.md Update

**Files:**
- Modify: `CONTRIBUTING.md`

**Step 243:** Update contributing guide for V2 patterns

**Step 244:** Commit
```bash
git commit -m "docs: update CONTRIBUTING.md for V2"
```

---

### Task 66: Verify Docs Build

**Step 245:** Build full documentation site:
Run: `cd website && npm ci && npm run build`

**Step 246:** Verify all pages render correctly

**Step 247:** Check for broken links

**Step 248:** Fix any issues

**Step 249:** Commit fixes

**Step 250:** Full docs commit
```bash
git commit -m "docs: verify and fix documentation build"
```

---

## Phase 6: System Refinements & Polish (Steps 251–290)

### Task 67: CI/CD — Fix Workflow Triggers

**Files:**
- Modify: `.github/workflows/ci.yml`

**Step 251:** Fix branch trigger from `main` to `master`

**Step 252:** Add integration test job

**Step 253:** Commit
```bash
git commit -m "ci: fix branch triggers and add integration tests"
```

---

### Task 68: CI/CD — NuGet Publish Workflow

**Files:**
- Create: `.github/workflows/nuget.yml`

**Step 254:** Create NuGet publish workflow:
- Triggers on tags `v*`
- Build Release
- Pack NuGet packages
- Push to NuGet.org

**Step 255:** Commit
```bash
git commit -m "ci: add NuGet publish workflow"
```

---

### Task 69: NuGet Package Metadata

**Files:**
- Modify: `Directory.Build.props`

**Step 256:** Add NuGet metadata to all packages:
- PackageId, Authors, Description
- RepositoryUrl, PackageProjectUrl
- PackageLicenseExpression
- PackageReadmeFile
- PackageTags

**Step 257:** Commit
```bash
git commit -m "build: add NuGet package metadata"
```

---

### Task 70: Package README Files

**Files:**
- Create: `src/Core/README.md`
- Create: `src/IAW.Testing/README.md`

**Step 258:** Create short README for each NuGet package

**Step 259:** Reference in csproj files

**Step 260:** Commit
```bash
git commit -m "docs: add NuGet package READMEs"
```

---

### Task 71: Update Directory.Packages.props

**Files:**
- Modify: `Directory.Packages.props`

**Step 261:** Verify all package versions are latest

**Step 262:** Remove unused package references

**Step 263:** Commit
```bash
git commit -m "build: update package versions"
```

---

### Task 72: Observability — Wire into V2

**Files:**
- Modify: `src/Core/Observability.cs`
- Modify: `src/Core/V2/AgentV2.cs`

**Step 264:** Ensure `AgentObservability` ActivitySource and meters work with V2

**Step 265:** Add counters for V2 operations (respond, memory, events, notifications)

**Step 266:** Commit
```bash
git commit -m "feat(v2): wire observability into AgentV2"
```

---

### Task 73: Update Sample Agents to V2

**Files:**
- Modify: all files in `samples/Samples/`

**Step 267:** Update Program.cs sample endpoints for V2 API

**Step 268:** Ensure all sample endpoints work

**Step 269:** Run samples and verify:
Run: `aspire run`

**Step 270:** Commit
```bash
git commit -m "refactor(v2): update all sample endpoints"
```

---

### Task 74: Update DevUI for V2

**Files:**
- Modify: `src/DevUI/Program.cs`

**Step 271:** Update DevUI agent registrations for V2

**Step 272:** Verify DevUI starts and works

**Step 273:** Commit
```bash
git commit -m "refactor(v2): update DevUI for V2"
```

---

### Task 75: AppHost — Clean Up

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`
- Modify: `src/IAW.AppHost/IAWExtensions.cs`

**Step 274:** Review and clean AppHost configuration

**Step 275:** Ensure all resources start correctly

**Step 276:** Commit
```bash
git commit -m "refactor: clean up AppHost configuration"
```

---

### Task 76: ServiceDefaults — Review

**Files:**
- Modify: `src/IAW.ServiceDefaults/Extensions.cs`

**Step 277:** Review OpenTelemetry config, health checks

**Step 278:** Commit
```bash
git commit -m "refactor: review service defaults"
```

---

### Task 77: global.json and SDK Version

**Files:**
- Modify: `global.json`

**Step 279:** Verify .NET 11 SDK version is latest preview

**Step 280:** Commit
```bash
git commit -m "build: update SDK version"
```

---

### Task 78: License and Legal

**Step 281:** Verify LICENSE file exists and is correct

**Step 282:** Verify all source files have no conflicting license headers

**Step 283:** Commit any fixes

---

### Task 79: Security Audit

**Step 284:** Check for hardcoded secrets in codebase

**Step 285:** Verify .gitignore covers secrets, build artifacts, IDE files

**Step 286:** Check for command injection in Shell agent and similar

**Step 287:** Commit any fixes
```bash
git commit -m "security: address audit findings"
```

---

### Task 80: Performance Check

**Step 288:** Run full test suite and note timing:
Run: `dotnet test IAW.slnx`

**Step 289:** Run Aspire and verify startup time is reasonable:
Run: `aspire run`

**Step 290:** Commit any optimizations
```bash
git commit -m "perf: address performance check findings"
```

---

## Phase 7: Final Integration & Release (Steps 291–310)

### Task 81: Full Build Verification

**Step 291:** Clean build:
Run: `dotnet clean IAW.slnx && dotnet build IAW.slnx`

**Step 292:** All unit tests:
Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`

**Step 293:** All integration tests:
Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`

**Step 294:** Aspire run all resources:
Run: `aspire run`

---

### Task 82: End-to-End Smoke Test

**Step 295:** Start Aspire, verify dashboard shows all resources green

**Step 296:** Test Telegram bot conversation flow

**Step 297:** Test MCP tools via Claude Code

**Step 298:** Test sample HTTP endpoints

**Step 299:** Test DevUI

**Step 300:** Test website builds and deploys

---

### Task 83: Documentation Final Review

**Step 301:** Read through all docs for accuracy

**Step 302:** Verify code examples in docs compile

**Step 303:** Check all internal links work

**Step 304:** Fix any issues found

---

### Task 84: Release Preparation

**Step 305:** Update version numbers in Directory.Build.props

**Step 306:** Final CHANGELOG entry

**Step 307:** Create git tag for release

**Step 308:** Build NuGet packages:
Run: `dotnet pack IAW.slnx -c Release`

**Step 309:** Verify packages contain expected content

**Step 310:** Create GitHub release with release notes
```bash
git tag v2.0.0
git push origin v2.0.0
```

---

## Summary

| Phase | Steps | Description |
|-------|-------|-------------|
| 1. AgentV2 Core | 1–60 | V2 contracts, base class, tests |
| 2. V1 Removal | 61–101 | Clean break, fix all compilation errors |
| 3. Telegram Bot | 102–160 | Full assistant hub on V2 |
| 4. MCP Server | 161–200 | All 7+ tools on V2 |
| 5. Documentation | 201–250 | Full docs site, tutorials, references |
| 6. System Polish | 251–290 | CI/CD, NuGet, security, performance |
| 7. Release | 291–310 | Final verification and release |

**Total: 310 steps across 84 tasks in 7 phases.**
