using Core;
using IAW.Testing.Scenario;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Testing;

public abstract class AgentTest<T> : IAsyncLifetime where T : class, IAgent
{
    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];

    protected TestCluster Cluster { get; private set; } = null!;
    protected IStreamProvider StreamProvider => Cluster.Client.GetStreamProvider("agents");
    protected MockChatClient MockLlm { get; } = new();
    protected ScenarioBuilder Scenario => new(id => Cluster.GrainFactory.GetGrain<IAgent>(id));

    protected IAgent Agent(string id) => Cluster.GrainFactory.GetGrain<IAgent>(id);
    protected string UniqueId(string prefix) => $"{prefix}-{_testRunId}";

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        ConfigureSilo(builder);
        Cluster = builder.Build();
        await Cluster.DeployAsync();
        await OnClusterReadyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Cluster.DisposeAsync();
    }

    protected virtual void ConfigureSilo(TestClusterBuilder builder) { }
    protected virtual Task OnClusterReadyAsync() => Task.CompletedTask;
    protected virtual Task OnBeforeTestAsync() => Task.CompletedTask;
    protected virtual Task OnAfterTestAsync() => Task.CompletedTask;
    protected virtual Task OnAgentActivatedAsync(IAgent agent) => Task.CompletedTask;

    // -- Metadata --

    [Fact]
    public async Task Behavior_Metadata_ReturnsIdAndCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("meta");
        var agent = Agent(agentId);

        var metadata = await agent.GetMetadataAsync(ct);

        Assert.Equal(agentId, metadata.Id);
        Assert.Contains("state", metadata.Capabilities);
        Assert.Contains("history", metadata.Capabilities);
        Assert.Contains("events", metadata.Capabilities);
        Assert.Contains("notifications", metadata.Capabilities);
        Assert.Contains("tracking", metadata.Capabilities);
        Assert.Contains("streams", metadata.Capabilities);
        Assert.Contains("tools", metadata.Capabilities);
    }

    // -- State --

    [Fact]
    public async Task Behavior_State_SetAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("state-get"));

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

        await agent.SetStateAsync("city", "Seattle", ct);
        await agent.SetStateAsync("temp", "21", ct);
        var state = await agent.GetStateAsync(ct);

        Assert.Equal("Seattle", state["city"]);
        Assert.Equal("21", state["temp"]);
    }

    // -- History --

    [Fact]
    public async Task Behavior_History_AddAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("history-get"));

        await agent.AddHistoryAsync("user", "hello", ct);
        await agent.AddHistoryAsync("assistant", "world", ct);
        var history = await agent.GetHistoryAsync(ct);

        Assert.Equal(2, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("hello", history[0].Content);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("world", history[1].Content);
    }

    [Fact]
    public async Task Behavior_History_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("history-stream");
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

    // -- Events --

    [Fact]
    public async Task Behavior_Events_PublishAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("events-get"));

        await agent.PublishEventAsync("evt.first", "payload-1", ct);
        await agent.PublishEventAsync("evt.second", "payload-2", ct);
        var events = await agent.GetEventsAsync(ct);

        Assert.Equal(2, events.Count);
        Assert.Equal("evt.first", events[0].Name);
        Assert.Equal("evt.second", events[1].Name);
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
        await agent.PublishEventAsync("evt.stream", "streamed", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("evt.stream", payload.Name);
        Assert.Equal("streamed", payload.Payload);
        await handle.UnsubscribeAsync();
    }

    // -- Notifications --

    [Fact]
    public async Task Behavior_Notifications_DeliveredToSubscriber()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("notif-pub");
        var subscriberId = UniqueId("notif-sub");
        var publisher = Agent(publisherId);
        var subscriber = Agent(subscriberId);

        await publisher.SubscribeAsync("alert", subscriberId, ct);
        await publisher.NotifyAsync("alert", "storm", ct);

        var notifications = await WaitHelpers.WaitForNotificationsAsync(subscriber, 1, ct);

        Assert.Single(notifications);
        Assert.Equal("alert", notifications[0].Topic);
        Assert.Equal("storm", notifications[0].Payload);
    }

    [Fact]
    public async Task Behavior_Notifications_EnvelopeMetadataPreserved()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("notif-env-pub");
        var subscriberId = UniqueId("notif-env-sub");
        var publisher = Agent(publisherId);
        var subscriber = Agent(subscriberId);
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        await publisher.SubscribeAsync("alert", subscriberId, ct);
        await publisher.NotifyAsync(new NotificationEnvelope
        {
            Topic = "alert",
            Payload = "{\"city\":\"Seattle\"}",
            ContentType = "application/json",
            Schema = "weather.alert",
            SchemaVersion = "1.0",
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, string>
            {
                ["source"] = "agent-test",
                ["tenant"] = "alpha"
            }
        }, ct);

        var notifications = await WaitHelpers.WaitForNotificationsAsync(subscriber, 1, ct);

        var entry = Assert.Single(notifications);
        Assert.Equal("alert", entry.Topic);
        Assert.Equal("{\"city\":\"Seattle\"}", entry.Payload);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("weather.alert", entry.Schema);
        Assert.Equal("1.0", entry.SchemaVersion);
        Assert.Equal(messageId, entry.MessageId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("agent-test", entry.Headers["source"]);
        Assert.Equal("alpha", entry.Headers["tenant"]);
    }

    [Fact]
    public async Task Behavior_Notifications_JsonPayloadRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("notif-json-pub");
        var subscriberId = UniqueId("notif-json-sub");
        var publisher = Agent(publisherId);
        var subscriber = Agent(subscriberId);

        await publisher.SubscribeAsync("alert", subscriberId, ct);
        await publisher.NotifyAsync(
            NotificationJson.CreateEnvelope(
                "alert",
                new TestPayload("Seattle", 42),
                schema: "test.payload",
                schemaVersion: "1.0"),
            ct);

        var notifications = await WaitHelpers.WaitForNotificationsAsync(subscriber, 1, ct);

        var entry = Assert.Single(notifications);
        var typed = entry.ReadPayload<TestPayload>();
        Assert.NotNull(typed);
        Assert.Equal("Seattle", typed!.City);
        Assert.Equal(42, typed.Value);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("test.payload", entry.Schema);
        Assert.Equal("1.0", entry.SchemaVersion);
    }

    [Fact]
    public async Task Behavior_Notifications_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("notif-stream-pub");
        var subscriberId = UniqueId("notif-stream-sub");
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
        await publisher.SubscribeAsync("alert", subscriberId, ct);
        await publisher.NotifyAsync("alert", "stream-payload", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("alert", payload.Topic);
        Assert.Equal("stream-payload", payload.Payload);
        await handle.UnsubscribeAsync();
    }

    // -- Tracking --

    [Fact]
    public async Task Behavior_Tracking_StartsAndStopsAtMaxTicks()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tracking-max"));

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

    // -- Tools --

    [Fact]
    public async Task Behavior_Tools_MissingToolThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tools-missing"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.InvokeToolAsync("nonexistent-tool", null, ct));
    }

    // -- Streams --

    [Fact]
    public async Task Behavior_Streams_PublishAndSubscribeRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("streams-custom"));
        var streamGuid = Guid.NewGuid();
        var stream = StreamProvider.GetStream<string>(StreamId.Create("agent-test-custom", streamGuid));
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((payload, _) =>
        {
            received.TrySetResult(payload);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.PublishStreamAsync("agent-test-custom", streamGuid, "hello-custom", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("hello-custom", payload);
        await handle.UnsubscribeAsync();
    }

    private sealed record TestPayload(string City, int Value);
}
