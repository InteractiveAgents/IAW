using Core;
using Core.V2;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Testing;

public abstract class AgentTestV2<T> : IAsyncLifetime where T : class, IAgentV2
{
    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];

    protected TestCluster Cluster { get; private set; } = null!;
    protected IStreamProvider StreamProvider => Cluster.Client.GetStreamProvider("agents");

    protected IAgentV2 AgentV2(string id)
    {
        // When T implements a specific grain interface beyond IAgentV2, resolve via that
        // to avoid ambiguity when multiple grain classes implement IAgentV2
        var specificInterface = typeof(T).GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgentV2) && typeof(IAgentV2).IsAssignableFrom(i) && typeof(IGrainWithStringKey).IsAssignableFrom(i));

        if (specificInterface is not null)
            return (IAgentV2)Cluster.GrainFactory.GetGrain(specificInterface, id);

        return Cluster.GrainFactory.GetGrain<IAgentV2>(id);
    }
    protected string UniqueId(string prefix) => $"{prefix}-{_testRunId}";

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentTestClientConfigurator>();
        ConfigureSilo(builder);
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Cluster.DisposeAsync();
    }

    protected virtual void ConfigureSilo(TestClusterBuilder builder) { }

    // -- Profile --

    [Fact]
    public async Task Behavior_Profile_ReturnsIdAndDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("profile");
        var agent = AgentV2(agentId);

        var profile = await agent.GetProfileAsync(ct);

        Assert.Equal(agentId, profile.Id);
        Assert.False(string.IsNullOrWhiteSpace(profile.DisplayName));
    }

    // -- Memory --

    [Fact]
    public async Task Behavior_Memory_SetAndGetRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("mem-get"));

        await agent.SetMemoryAsync("key", "val", ct);
        var value = await agent.GetMemoryAsync("key", ct);

        Assert.Equal("val", value);
    }

    [Fact]
    public async Task Behavior_Memory_GetMissing_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("mem-miss"));

        var value = await agent.GetMemoryAsync("nonexistent", ct);

        Assert.Null(value);
    }

    // -- Messages --

    [Fact]
    public async Task Behavior_Messages_AppendAndQueryRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("msg-get"));

        await agent.AppendMessageAsync(new AgentMessage { Role = "user", Content = "hello" }, ct);
        await agent.AppendMessageAsync(new AgentMessage { Role = "assistant", Content = "world" }, ct);
        var messages = await agent.QueryMessagesAsync(ct: ct);

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("hello", messages[0].Content);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("world", messages[1].Content);
    }

    [Fact]
    public async Task Behavior_Messages_QueryWithFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("msg-filter"));

        await agent.AppendMessageAsync(new AgentMessage { Role = "user", Content = "msg1" }, ct);
        await agent.AppendMessageAsync(new AgentMessage { Role = "user", Content = "msg2" }, ct);
        await agent.AppendMessageAsync(new AgentMessage { Role = "assistant", Content = "msg3" }, ct);

        var userMessages = await agent.QueryMessagesAsync(new AgentMessageQuery { Role = "user" }, ct);

        Assert.Equal(2, userMessages.Count);
        Assert.All(userMessages, m => Assert.Equal("user", m.Role));
    }

    [Fact]
    public async Task Behavior_Messages_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("msg-stream");
        var agent = AgentV2(agentId);
        var stream = StreamProvider.GetStream<AgentMessage>(StreamId.Create("agent-history", agentId));
        var received = new TaskCompletionSource<AgentMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.AppendMessageAsync(new AgentMessage { Role = "user", Content = "stream-test" }, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("user", payload.Role);
        Assert.Equal("stream-test", payload.Content);
        await handle.UnsubscribeAsync();
    }

    // -- Events --

    [Fact]
    public async Task Behavior_Events_AppendAndQueryRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("evt-get"));

        await agent.AppendEventAsync(new AgentEvent { Type = "evt.first", Payload = "p1" }, ct);
        await agent.AppendEventAsync(new AgentEvent { Type = "evt.second", Payload = "p2" }, ct);
        var events = await agent.QueryEventsAsync(ct: ct);

        Assert.Equal(2, events.Count);
        Assert.Equal("evt.first", events[0].Type);
        Assert.Equal("evt.second", events[1].Type);
    }

    [Fact]
    public async Task Behavior_Events_QueryWithTypeFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("evt-filter"));

        await agent.AppendEventAsync(new AgentEvent { Type = "info", Payload = "a" }, ct);
        await agent.AppendEventAsync(new AgentEvent { Type = "warn", Payload = "b" }, ct);
        await agent.AppendEventAsync(new AgentEvent { Type = "info", Payload = "c" }, ct);

        var infoEvents = await agent.QueryEventsAsync(new AgentEventQuery { Type = "info" }, ct);

        Assert.Equal(2, infoEvents.Count);
        Assert.All(infoEvents, e => Assert.Equal("info", e.Type));
    }

    [Fact]
    public async Task Behavior_Events_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("evt-stream");
        var agent = AgentV2(agentId);
        var stream = StreamProvider.GetStream<AgentEvent>(StreamId.Create("agent-events", agentId));
        var received = new TaskCompletionSource<AgentEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.AppendEventAsync(new AgentEvent { Type = "evt.stream", Payload = "streamed" }, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("evt.stream", payload.Type);
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
        var publisher = AgentV2(publisherId);
        var subscriber = AgentV2(subscriberId);

        await publisher.SubscribeAsync("alert", subscriberId, ct);
        await publisher.NotifyAsync(new NotificationEnvelope { Topic = "alert", Payload = "storm" }, ct);

        var notifications = await WaitHelpersV2.WaitForNotificationsV2Async(subscriber, 1, ct);

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
        var publisher = AgentV2(publisherId);
        var subscriber = AgentV2(subscriberId);
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

        var notifications = await WaitHelpersV2.WaitForNotificationsV2Async(subscriber, 1, ct);

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
    public async Task Behavior_Notifications_EmitsStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisherId = UniqueId("notif-stream-pub");
        var subscriberId = UniqueId("notif-stream-sub");
        var publisher = AgentV2(publisherId);
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
        await publisher.NotifyAsync(new NotificationEnvelope { Topic = "alert", Payload = "stream-payload" }, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("alert", payload.Topic);
        Assert.Equal("stream-payload", payload.Payload);
        await handle.UnsubscribeAsync();
    }

    // -- Scheduling --

    [Fact]
    public async Task Behavior_Scheduling_StartsAndStopsAtMaxTicks()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("sched-max"));

        await agent.StartScheduleAsync(TimeSpan.FromMilliseconds(40), 3, ct);
        var status = await WaitHelpersV2.WaitForScheduleToStopAsync(agent, ct);

        Assert.False(status.IsRunning);
        Assert.Equal(3, status.TickCount);
    }

    [Fact]
    public async Task Behavior_Scheduling_ReminderIntervalStartsWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("sched-reminder"));

        await agent.StartScheduleAsync(TimeSpan.FromMinutes(1), 2, ct);
        var status = await agent.GetScheduleStatusAsync(ct);

        Assert.True(status.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(1), status.Interval);
        Assert.Equal(2, status.MaxTicks);

        await agent.StopScheduleAsync(ct);
    }

    // -- Tools --

    [Fact]
    public async Task Behavior_Tools_MissingToolThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("tools-missing"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.InvokeToolAsync("nonexistent-tool", null, ct));
    }

    // -- Streams --

    [Fact]
    public async Task Behavior_Streams_PublishAndSubscribeRoundtrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = AgentV2(UniqueId("streams-custom"));
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
}
