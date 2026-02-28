using Core;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Agents.Tests;

public sealed class OrleansAgentGrainBehaviorTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentsSiloConfigurator>();
        builder.AddClientBuilderConfigurator<AgentsClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.DisposeAsync();
    }

    [Fact]
    public async Task Metadata_ReturnsExpectedCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _cluster.GrainFactory.GetGrain<IAgent>("meta-1");

        var metadata = await agent.GetMetadataAsync(ct);

        Assert.Equal("meta-1", metadata.Id);
        Assert.Contains("state", metadata.Capabilities);
        Assert.Contains("streams", metadata.Capabilities);
    }

    [Fact]
    public async Task State_And_Increment_ArePersisted()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _cluster.GrainFactory.GetGrain<IAgent>("state-1");

        await agent.SetStateAsync("city", "Seattle", ct);
        var visit1 = await agent.IncrementAsync("visits", ct);
        var visit2 = await agent.IncrementAsync("visits", ct);
        var state = await agent.GetStateAsync(ct);

        Assert.Equal(1, visit1);
        Assert.Equal(2, visit2);
        Assert.Equal("Seattle", state["city"]);
        Assert.Equal("2", state["visits"]);
    }

    [Fact]
    public async Task Events_AreRecordedInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _cluster.GrainFactory.GetGrain<IAgent>("events-1");

        await agent.PublishEventAsync("weather.refresh", "Seattle", ct);
        await agent.PublishEventAsync("weather.alert", "rain", ct);
        var events = await agent.GetEventsAsync(ct);

        Assert.Equal(2, events.Count);
        Assert.Equal("weather.refresh", events[0].Name);
        Assert.Equal("weather.alert", events[1].Name);
    }

    [Fact]
    public async Task PublishEvent_EmitsAgentEventStream()
    {
        var ct = TestContext.Current.CancellationToken;
        const string agentId = "events-stream-1";
        var agent = _cluster.GrainFactory.GetGrain<IAgent>(agentId);
        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<AgentEventRecord>(StreamId.Create("agent-events", agentId));
        var received = new TaskCompletionSource<AgentEventRecord>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.PublishEventAsync("weather.refresh", "Seattle", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("weather.refresh", payload.Name);
        Assert.Equal("Seattle", payload.Payload);
        await handle.UnsubscribeAsync();
    }

    [Fact]
    public async Task AddHistory_EmitsAgentHistoryStream()
    {
        var ct = TestContext.Current.CancellationToken;
        const string agentId = "history-stream-1";
        var agent = _cluster.GrainFactory.GetGrain<IAgent>(agentId);
        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<AgentHistoryEntry>(StreamId.Create("agent-history", agentId));
        var received = new TaskCompletionSource<AgentHistoryEntry>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await agent.AddHistoryAsync("user", "hello-stream-history", ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var payload = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("user", payload.Role);
        Assert.Equal("hello-stream-history", payload.Content);
        await handle.UnsubscribeAsync();
    }

    [Fact]
    public async Task Notify_EmitsAgentNotificationStream()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string payload = "storm";
        const string subscriberId = "subscriber-stream-1";

        var publisher = _cluster.GrainFactory.GetGrain<IAgent>("publisher-stream-1");
        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<NotificationRecord>(
            StreamId.Create("agent-notifications", subscriberId));
        var received = new TaskCompletionSource<NotificationRecord>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = await stream.SubscribeAsync((entry, _) =>
        {
            received.TrySetResult(entry);
            return Task.CompletedTask;
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await publisher.SubscribeAsync(topic, subscriberId, ct);
        await publisher.NotifyAsync(topic, payload, ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var entry = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal(topic, entry.Topic);
        Assert.Equal(payload, entry.Payload);
        await handle.UnsubscribeAsync();
    }

    [Fact]
    public async Task Notify_DeliversToSubscribers()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisher = _cluster.GrainFactory.GetGrain<IAgent>("publisher-1");
        var subscriber = _cluster.GrainFactory.GetGrain<IAgent>("subscriber-1");

        await publisher.SubscribeAsync("weather.alert", "subscriber-1", ct);
        await publisher.NotifyAsync("weather.alert", "storm", ct);
        var notifications = await subscriber.GetNotificationsAsync(ct);

        Assert.Single(notifications);
        Assert.Equal("weather.alert", notifications[0].Topic);
        Assert.Equal("storm", notifications[0].Payload);
    }

    [Fact]
    public async Task Notify_WithEnvelope_DeliversMetadataToSubscribers()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string subscriberId = "subscriber-envelope-1";

        var publisher = _cluster.GrainFactory.GetGrain<IAgent>("publisher-envelope-1");
        var subscriber = _cluster.GrainFactory.GetGrain<IAgent>(subscriberId);
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        await publisher.SubscribeAsync(topic, subscriberId, ct);
        await publisher.NotifyAsync(new NotificationEnvelope
        {
            Topic = topic,
            Payload = "{\"city\":\"Seattle\",\"severity\":\"high\"}",
            ContentType = "application/json",
            Schema = "weather.alert",
            SchemaVersion = "1.0",
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, string>
            {
                ["source"] = "agents-tests",
                ["tenant"] = "alpha"
            }
        }, ct);

        var notifications = await subscriber.GetNotificationsAsync(ct);

        var entry = Assert.Single(notifications);
        Assert.Equal(topic, entry.Topic);
        Assert.Equal("{\"city\":\"Seattle\",\"severity\":\"high\"}", entry.Payload);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("weather.alert", entry.Schema);
        Assert.Equal("1.0", entry.SchemaVersion);
        Assert.Equal(messageId, entry.MessageId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("agents-tests", entry.Headers["source"]);
        Assert.Equal("alpha", entry.Headers["tenant"]);
    }

    [Fact]
    public async Task Notify_WithJsonHelper_DeliversTypedPayloadToSubscriber()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string subscriberId = "subscriber-json-1";
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        var publisher = _cluster.GrainFactory.GetGrain<IAgent>("publisher-json-1");
        var subscriber = _cluster.GrainFactory.GetGrain<IAgent>(subscriberId);

        await publisher.SubscribeAsync(topic, subscriberId, ct);
        await publisher.NotifyAsync(
            NotificationJson.CreateEnvelope(
                topic,
                new WeatherAlertPayload("Seattle", "critical", 6),
                schema: "weather.alert",
                schemaVersion: "2.0",
                messageId: messageId,
                correlationId: correlationId,
                headers: new Dictionary<string, string>
                {
                    ["source"] = "agents-tests-json",
                    ["tenant"] = "alpha"
                }),
            ct);

        var notifications = await subscriber.GetNotificationsAsync(ct);
        var entry = Assert.Single(notifications);
        var typedPayload = entry.ReadPayload<WeatherAlertPayload>();

        Assert.Equal(topic, entry.Topic);
        Assert.NotNull(typedPayload);
        Assert.Equal("Seattle", typedPayload!.City);
        Assert.Equal("critical", typedPayload.Severity);
        Assert.Equal(6, typedPayload.TemperatureC);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("weather.alert", entry.Schema);
        Assert.Equal("2.0", entry.SchemaVersion);
        Assert.Equal(messageId, entry.MessageId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("agents-tests-json", entry.Headers["source"]);
        Assert.Equal("alpha", entry.Headers["tenant"]);
    }

    [Fact]
    public async Task Tracking_StartsTicks_AndStopsAtMax()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _cluster.GrainFactory.GetGrain<IAgent>("tracking-1");

        await agent.StartTrackingAsync(TimeSpan.FromMilliseconds(40), 3, ct);
        var status = await WaitForTrackingToStopAsync(agent, ct);

        Assert.False(status.IsTracking);
        Assert.Equal(3, status.TickCount);
    }

    [Fact]
    public async Task Tracking_WithReminderInterval_StartsWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _cluster.GrainFactory.GetGrain<IAgent>("tracking-reminder-1");

        await agent.StartTrackingAsync(TimeSpan.FromMinutes(1), 2, ct);
        var status = await agent.GetTrackingStatusAsync(ct);

        Assert.True(status.IsTracking);
        Assert.Equal(TimeSpan.FromMinutes(1), status.Interval);
        Assert.Equal(2, status.MaxTicks);

        await agent.StopTrackingAsync(ct);
    }

    [Fact]
    public async Task StreamPublish_IsReceivedByClientSubscription()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _cluster.GrainFactory.GetGrain<IAgent>("stream-1");

        var streamProvider = _cluster.Client.GetStreamProvider("agents");
        var streamGuid = Guid.NewGuid();
        var streamId = StreamId.Create("agent-tests", streamGuid);
        var stream = streamProvider.GetStream<string>(streamId);
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

    private static async Task<AgentTrackingStatus> WaitForTrackingToStopAsync(
        IAgent agent,
        CancellationToken ct)
    {
        for (var i = 0; i < 80; i++)
        {
            var status = await agent.GetTrackingStatusAsync(ct);
            if (!status.IsTracking)
            {
                return status;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
        }

        throw new TimeoutException("Tracking did not stop in time.");
    }

    private sealed record WeatherAlertPayload(string City, string Severity, int TemperatureC);
}

