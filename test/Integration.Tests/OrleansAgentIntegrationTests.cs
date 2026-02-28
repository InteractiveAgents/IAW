using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Streams;
using System.Net;
using System.Text.Json;
using Xunit;

namespace IAW.Integration.Tests;

public sealed class OrleansAgentIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private HttpClient _samplesClient = null!;
    private IHost _orleansClientHost = null!;
    private IClusterClient _orleansClient = null!;
    private Uri _orleansGatewayEndpoint = null!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>(
            ["--Parameters:anthropic-api-key=test-key"]);

        _app = await appHost.BuildAsync();

        using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.StartAsync(startTimeout.Token);
        await _app.ResourceNotifications
            .WaitForResourceAsync("samples", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60), startTimeout.Token);

        _samplesClient = _app.CreateHttpClient("samples");
        _orleansGatewayEndpoint = _app.GetEndpoint("samples", "orleans-gateway");

        _orleansClientHost = Host.CreateApplicationBuilder()
            .UseOrleansClient(client =>
            {
                client.UseLocalhostClustering(
                    gatewayPort: _orleansGatewayEndpoint.Port,
                    serviceId: "default",
                    clusterId: "default");
                client.AddMemoryStreams("agents");
            })
            .Build();

        await _orleansClientHost.StartAsync(startTimeout.Token);
        _orleansClient = _orleansClientHost.Services.GetRequiredService<IClusterClient>();
    }

    public async ValueTask DisposeAsync()
    {
        _samplesClient.Dispose();
        await _orleansClientHost.StopAsync();
        _orleansClientHost.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public void AspireTestingHost_ExposesOrleansTestResource()
    {
        Assert.False(string.IsNullOrWhiteSpace(_orleansGatewayEndpoint.Host));
        Assert.True(_orleansGatewayEndpoint.Port > 0);
    }

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
        Assert.Equal(21, legacyState.GetProperty("temperatureC").GetInt32());
        Assert.Equal(-999, legacyState.GetProperty("missingInt").GetInt32());
        Assert.True(legacyState.GetProperty("hasIsRaining").GetBoolean());
        Assert.True(legacyState.GetProperty("isRaining").GetBoolean());

        var legacyIdentity = await GetJsonAsync("/samples/agent/identity", ct);
        Assert.False(string.IsNullOrWhiteSpace(legacyIdentity.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(legacyIdentity.GetProperty("displayName").GetString()));

        var legacyMetadata = await GetJsonAsync("/samples/agent/metadata", ct);
        var weatherLegacyMetadata = legacyMetadata.GetProperty("weather");
        Assert.False(string.IsNullOrWhiteSpace(weatherLegacyMetadata.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(weatherLegacyMetadata.GetProperty("displayName").GetString()));
        var legacyCapabilities = weatherLegacyMetadata
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        Assert.Contains("state", legacyCapabilities);
        Assert.Contains("streams", legacyCapabilities);

        var legacyTracking = await GetJsonAsync("/samples/agent/tracking", ct);
        Assert.True(legacyTracking.GetProperty("increasedWhileRunning").GetBoolean());
        Assert.True(legacyTracking.GetProperty("stoppedTicking").GetBoolean());

        var legacyStreaming = await GetJsonAsync("/samples/agent/streaming", ct);
        Assert.True(legacyStreaming.GetProperty("publishAccepted").GetBoolean());
        Assert.True(legacyStreaming.GetProperty("deliveredBoth").GetBoolean());
        Assert.Equal(0, legacyStreaming.GetProperty("subscriberCountAfter").GetInt32());

        var legacyEvents = await GetJsonAsync("/samples/agent/events/publish", ct);
        Assert.Equal(2, legacyEvents.GetProperty("publishedCount").GetInt32());
        Assert.Equal(2, legacyEvents.GetProperty("handledCount").GetInt32());

        var legacyNotifications = await GetJsonAsync("/samples/agent/notifications", ct);
        Assert.Equal(2, legacyNotifications.GetProperty("subscriptionCounts").GetProperty("weather.alert").GetInt32());
        Assert.Equal(1, legacyNotifications.GetProperty("subscriptionCounts").GetProperty("weather.refresh").GetInt32());

        var isolation = await GetJsonAsync("/samples/orleans-agent/state-isolation", ct);
        Assert.True(isolation.GetProperty("isolated").GetBoolean());

        var metadata = await GetJsonAsync("/samples/orleans-agent/metadata", ct);
        var capabilities = metadata
            .GetProperty("capabilities")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        Assert.Contains("state", capabilities);
        Assert.Contains("streams", capabilities);

        var eventsPayload = await GetJsonAsync("/samples/orleans-agent/events", ct);
        Assert.Equal(2, eventsPayload.GetProperty("count").GetInt32());

        var notifications = await GetJsonAsync("/samples/orleans-agent/notifications", ct);
        Assert.Equal(1, notifications.GetProperty("count").GetInt32());
        Assert.Equal("weather.alert", notifications.GetProperty("topic").GetString());
        Assert.Equal("storm", notifications.GetProperty("payload").GetString());

        var notificationsEnvelope = await GetJsonAsync("/samples/orleans-agent/notifications-envelope", ct);
        Assert.Equal(1, notificationsEnvelope.GetProperty("count").GetInt32());
        Assert.Equal("weather.alert", notificationsEnvelope.GetProperty("topic").GetString());
        Assert.Equal("application/json", notificationsEnvelope.GetProperty("contentType").GetString());

        var notificationsDynamic = await GetJsonAsync("/samples/orleans-agent/notifications-dynamic", ct);
        Assert.Equal(1, notificationsDynamic.GetProperty("count").GetInt32());
        Assert.Equal("weather.alert", notificationsDynamic.GetProperty("topic").GetString());
        Assert.Equal("Seattle", notificationsDynamic.GetProperty("city").GetString());
        Assert.Equal("critical", notificationsDynamic.GetProperty("severity").GetString());
        Assert.Equal(6, notificationsDynamic.GetProperty("temperatureC").GetInt32());
        Assert.Equal("application/json", notificationsDynamic.GetProperty("contentType").GetString());

        var tracking = await GetJsonAsync("/samples/orleans-agent/tracking", ct);
        Assert.False(tracking.GetProperty("isTracking").GetBoolean());
        Assert.Equal(3, tracking.GetProperty("tickCount").GetInt32());

        var stream = await GetJsonAsync("/samples/orleans-agent/stream", ct);
        Assert.True(stream.GetProperty("published").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(stream.GetProperty("payload").GetString()));
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

        var state = await _orleansClient.GetGrain<IAgent>(agentId).GetStateAsync(ct);
        Assert.Equal("Seattle", state["city"]);
        Assert.Equal("4", state["visits"]);
    }

    [Fact]
    public async Task OrleansNotificationsEnvelopeEndpoint_DeliversMetadata()
    {
        var ct = TestContext.Current.CancellationToken;

        var payload = await GetJsonAsync("/samples/orleans-agent/notifications-envelope", ct);

        Assert.Equal(1, payload.GetProperty("count").GetInt32());
        Assert.Equal("weather.alert", payload.GetProperty("topic").GetString());
        Assert.Equal("{\"city\":\"Seattle\",\"severity\":\"high\"}", payload.GetProperty("payload").GetString());
        Assert.Equal("application/json", payload.GetProperty("contentType").GetString());
        Assert.Equal("weather.alert", payload.GetProperty("schema").GetString());
        Assert.Equal("1.0", payload.GetProperty("schemaVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("messageId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("correlationId").GetString()));
        Assert.Equal("samples", payload.GetProperty("sourceHeader").GetString());
        Assert.Equal("alpha", payload.GetProperty("tenantHeader").GetString());
    }

    [Fact]
    public async Task OrleansNotificationsDynamicEndpoint_RoundTripsTypedPayload()
    {
        var ct = TestContext.Current.CancellationToken;

        var payload = await GetJsonAsync("/samples/orleans-agent/notifications-dynamic", ct);

        Assert.Equal(1, payload.GetProperty("count").GetInt32());
        Assert.Equal("weather.alert", payload.GetProperty("topic").GetString());
        Assert.Equal("Seattle", payload.GetProperty("city").GetString());
        Assert.Equal("critical", payload.GetProperty("severity").GetString());
        Assert.Equal(6, payload.GetProperty("temperatureC").GetInt32());
        Assert.Equal("application/json", payload.GetProperty("contentType").GetString());
        Assert.Equal("weather.alert", payload.GetProperty("schema").GetString());
        Assert.Equal("2.0", payload.GetProperty("schemaVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("messageId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("correlationId").GetString()));
        Assert.Equal("samples", payload.GetProperty("sourceHeader").GetString());
        Assert.Equal("beta", payload.GetProperty("tenantHeader").GetString());
    }

    [Fact]
    public async Task LegacyStreamingEndpoint_DeliversExpectedMessagesInOrder()
    {
        var ct = TestContext.Current.CancellationToken;

        var legacyStreaming = await GetJsonAsync("/samples/agent/streaming", ct);
        Assert.Equal("weather.updates", legacyStreaming.GetProperty("topic").GetString());
        Assert.True(legacyStreaming.GetProperty("publishAccepted").GetBoolean());
        Assert.True(legacyStreaming.GetProperty("deliveredBoth").GetBoolean());
        Assert.Equal(0, legacyStreaming.GetProperty("subscriberCountAfter").GetInt32());

        var received = legacyStreaming
            .GetProperty("received")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(["weather:rain", "weather:sun"], received);
    }

    [Fact]
    public async Task OrleansStreamEndpoint_DeliversPublishedPayload()
    {
        var ct = TestContext.Current.CancellationToken;

        var stream = await GetJsonAsync("/samples/orleans-agent/stream", ct);
        Assert.True(stream.GetProperty("published").GetBoolean());
        Assert.True(stream.GetProperty("delivered").GetBoolean());
        Assert.Equal(0, stream.GetProperty("subscriberCountAfter").GetInt32());

        var payload = stream.GetProperty("payload").GetString();
        var received = stream.GetProperty("received").GetString();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task OrleansEventProcessingScenario_CompletesEndToEnd()
    {
        var ct = TestContext.Current.CancellationToken;

        var scenario = await GetJsonAsync("/samples/orleans-agent/event-processing", ct);
        Assert.Equal("orleans-streams", scenario.GetProperty("deliveryMechanism").GetString());
        Assert.Equal("weather.alert", scenario.GetProperty("topic").GetString());
        Assert.Equal("agent-event-processing", scenario.GetProperty("streamNamespace").GetString());
        Assert.True(scenario.GetProperty("processed").GetBoolean());
        Assert.Equal(1, scenario.GetProperty("processorNotificationCount").GetInt32());
        Assert.Equal(1, scenario.GetProperty("processedCount").GetInt32());

        var payload = scenario.GetProperty("payload").GetString();
        var lastProcessedPayload = scenario.GetProperty("lastProcessedPayload").GetString();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        Assert.Equal(payload, lastProcessedPayload);

        var processorEventNames = scenario
            .GetProperty("processorEventNames")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        Assert.Contains("processing.completed", processorEventNames);
    }

    [Fact]
    public async Task AspireEndpointDiscovery_CanRunAgentEventProcessingE2E()
    {
        var ct = TestContext.Current.CancellationToken;
        var samplesEndpoint = _app.GetEndpoint("samples");
        using var client = new HttpClient
        {
            BaseAddress = samplesEndpoint
        };

        var scenario = await GetJsonAsync(client, "/samples/orleans-agent/event-processing", ct);
        Assert.Equal("orleans-streams", scenario.GetProperty("deliveryMechanism").GetString());
        Assert.True(scenario.GetProperty("processed").GetBoolean());
        Assert.Equal(1, scenario.GetProperty("processedCount").GetInt32());
    }

    [Fact]
    public async Task OrleansClient_StreamEventProcessing_CompletesEndToEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string streamNamespace = "agent-event-processing-direct";
        var streamId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { city = "Seattle", severity = "high", source = "direct-client" });

        var processor = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-{Guid.NewGuid():N}");
        var streamProvider = _orleansClient.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));

        var handle = await stream.SubscribeAsync(async (message, _) =>
        {
            await processor.ReceiveNotificationAsync(topic, message, ct);
            await processor.SetStateAsync("last-processed-topic", topic, ct);
            await processor.SetStateAsync("last-processed-payload", message, ct);
            await processor.IncrementAsync("processed-count", ct);
            await processor.PublishEventAsync("processing.completed", message, ct);
        });

        await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
        await stream.OnNextAsync(payload);

        var processedCount = await WaitForProcessedCountAsync(processor, 1, ct);
        var notifications = await processor.GetNotificationsAsync(ct);
        var events = await processor.GetEventsAsync(ct);
        var lastProcessedPayload = await processor.GetStateValueAsync("last-processed-payload", ct);
        await handle.UnsubscribeAsync();

        Assert.Equal(1, processedCount);
        var notification = Assert.Single(notifications);
        Assert.Equal(topic, notification.Topic);
        Assert.Equal(payload, notification.Payload);
        Assert.Equal(payload, lastProcessedPayload);
        Assert.Contains(events, entry => string.Equals(entry.Name, "processing.completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OrleansClient_StreamEventProcessing_SinglePublishRemainsSingleProcessed()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string streamNamespace = "agent-event-processing-direct-single";
        var streamId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { city = "Seattle", severity = "high", source = "single-publish-check" });

        var processor = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-single-{Guid.NewGuid():N}");
        var streamProvider = _orleansClient.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));

        var handle = await stream.SubscribeAsync(async (message, _) =>
        {
            await processor.ReceiveNotificationAsync(topic, message, ct);
            await processor.SetStateAsync("last-processed-topic", topic, ct);
            await processor.SetStateAsync("last-processed-payload", message, ct);
            await processor.IncrementAsync("processed-count", ct);
            await processor.PublishEventAsync("processing.completed", message, ct);
        });

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
            await stream.OnNextAsync(payload);
            _ = await WaitForProcessedCountAsync(processor, 1, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            var processedCountRaw = await processor.GetStateValueAsync("processed-count", ct);
            var processedCount = int.TryParse(processedCountRaw, out var parsedProcessedCount) ? parsedProcessedCount : 0;
            var notifications = await processor.GetNotificationsAsync(ct);
            var events = await processor.GetEventsAsync(ct);
            var completedEventsCount = events.Count(entry =>
                string.Equals(entry.Name, "processing.completed", StringComparison.Ordinal));

            Assert.Equal(1, processedCount);
            Assert.Single(notifications);
            Assert.Equal(1, completedEventsCount);
        }
        finally
        {
            await handle.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task OrleansClient_StreamEventProcessing_SinglePublishProcessesEachSubscriberOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string streamNamespace = "agent-event-processing-direct-dual";
        var streamId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { city = "Seattle", severity = "high", source = "dual-subscriber-check" });

        var processorA = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-a-{Guid.NewGuid():N}");
        var processorB = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-b-{Guid.NewGuid():N}");
        var streamProvider = _orleansClient.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));

        var handleA = await stream.SubscribeAsync(async (message, _) =>
        {
            await processorA.ReceiveNotificationAsync(topic, message, ct);
            await processorA.SetStateAsync("last-processed-topic", topic, ct);
            await processorA.SetStateAsync("last-processed-payload", message, ct);
            await processorA.IncrementAsync("processed-count", ct);
            await processorA.PublishEventAsync("processing.completed", message, ct);
        });

        var handleB = await stream.SubscribeAsync(async (message, _) =>
        {
            await processorB.ReceiveNotificationAsync(topic, message, ct);
            await processorB.SetStateAsync("last-processed-topic", topic, ct);
            await processorB.SetStateAsync("last-processed-payload", message, ct);
            await processorB.IncrementAsync("processed-count", ct);
            await processorB.PublishEventAsync("processing.completed", message, ct);
        });

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
            await stream.OnNextAsync(payload);
            _ = await WaitForProcessedCountAsync(processorA, 1, ct);
            _ = await WaitForProcessedCountAsync(processorB, 1, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            var processedCountARaw = await processorA.GetStateValueAsync("processed-count", ct);
            var processedCountA = int.TryParse(processedCountARaw, out var parsedProcessedCountA) ? parsedProcessedCountA : 0;
            var notificationsA = await processorA.GetNotificationsAsync(ct);
            var eventsA = await processorA.GetEventsAsync(ct);
            var completedEventsCountA = eventsA.Count(entry =>
                string.Equals(entry.Name, "processing.completed", StringComparison.Ordinal));

            var processedCountBRaw = await processorB.GetStateValueAsync("processed-count", ct);
            var processedCountB = int.TryParse(processedCountBRaw, out var parsedProcessedCountB) ? parsedProcessedCountB : 0;
            var notificationsB = await processorB.GetNotificationsAsync(ct);
            var eventsB = await processorB.GetEventsAsync(ct);
            var completedEventsCountB = eventsB.Count(entry =>
                string.Equals(entry.Name, "processing.completed", StringComparison.Ordinal));

            Assert.Equal(1, processedCountA);
            var notificationA = Assert.Single(notificationsA);
            Assert.Equal(topic, notificationA.Topic);
            Assert.Equal(payload, notificationA.Payload);
            Assert.Equal(1, completedEventsCountA);

            Assert.Equal(1, processedCountB);
            var notificationB = Assert.Single(notificationsB);
            Assert.Equal(topic, notificationB.Topic);
            Assert.Equal(payload, notificationB.Payload);
            Assert.Equal(1, completedEventsCountB);
        }
        finally
        {
            await handleA.UnsubscribeAsync();
            await handleB.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task OrleansClient_StreamEventProcessing_NoSubscribersProducesNoProcessing()
    {
        var ct = TestContext.Current.CancellationToken;
        const string streamNamespace = "agent-event-processing-direct-none";
        var streamId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { city = "Seattle", severity = "high", source = "no-subscriber-check" });

        var processor = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-none-{Guid.NewGuid():N}");
        var streamProvider = _orleansClient.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));

        await stream.OnNextAsync(payload);
        await Task.Delay(TimeSpan.FromMilliseconds(250), ct);

        var processedCountRaw = await processor.GetStateValueAsync("processed-count", ct);
        var processedCount = int.TryParse(processedCountRaw, out var parsedProcessedCount) ? parsedProcessedCount : 0;
        var notifications = await processor.GetNotificationsAsync(ct);
        var events = await processor.GetEventsAsync(ct);

        Assert.Equal(0, processedCount);
        Assert.Empty(notifications);
        Assert.Empty(events);
    }

    [Fact]
    public async Task OrleansClient_StreamEventProcessing_DualSubscribers_PreserveOrderAndProcessEachOncePerMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        const string streamNamespace = "agent-event-processing-direct-dual-ordered";
        var streamId = Guid.NewGuid();
        var payload1 = JsonSerializer.Serialize(new { city = "Seattle", severity = "low", sequence = 1 });
        var payload2 = JsonSerializer.Serialize(new { city = "Seattle", severity = "high", sequence = 2 });

        var processorA = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-a-ordered-{Guid.NewGuid():N}");
        var processorB = _orleansClient.GetGrain<IAgent>($"integration-direct-processor-b-ordered-{Guid.NewGuid():N}");
        var streamProvider = _orleansClient.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));

        var handleA = await stream.SubscribeAsync(async (message, _) =>
        {
            await processorA.ReceiveNotificationAsync(topic, message, ct);
            await processorA.SetStateAsync("last-processed-topic", topic, ct);
            await processorA.SetStateAsync("last-processed-payload", message, ct);
            await processorA.IncrementAsync("processed-count", ct);
            await processorA.PublishEventAsync("processing.completed", message, ct);
        });

        var handleB = await stream.SubscribeAsync(async (message, _) =>
        {
            await processorB.ReceiveNotificationAsync(topic, message, ct);
            await processorB.SetStateAsync("last-processed-topic", topic, ct);
            await processorB.SetStateAsync("last-processed-payload", message, ct);
            await processorB.IncrementAsync("processed-count", ct);
            await processorB.PublishEventAsync("processing.completed", message, ct);
        });

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
            await stream.OnNextAsync(payload1);
            await stream.OnNextAsync(payload2);
            _ = await WaitForProcessedCountAsync(processorA, 2, ct);
            _ = await WaitForProcessedCountAsync(processorB, 2, ct);

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            var processedCountARaw = await processorA.GetStateValueAsync("processed-count", ct);
            var processedCountA = int.TryParse(processedCountARaw, out var parsedProcessedCountA) ? parsedProcessedCountA : 0;
            var notificationsA = await processorA.GetNotificationsAsync(ct);
            var eventsA = await processorA.GetEventsAsync(ct);
            var completedEventsCountA = eventsA.Count(entry =>
                string.Equals(entry.Name, "processing.completed", StringComparison.Ordinal));

            var processedCountBRaw = await processorB.GetStateValueAsync("processed-count", ct);
            var processedCountB = int.TryParse(processedCountBRaw, out var parsedProcessedCountB) ? parsedProcessedCountB : 0;
            var notificationsB = await processorB.GetNotificationsAsync(ct);
            var eventsB = await processorB.GetEventsAsync(ct);
            var completedEventsCountB = eventsB.Count(entry =>
                string.Equals(entry.Name, "processing.completed", StringComparison.Ordinal));

            Assert.Equal(2, processedCountA);
            Assert.Equal(2, completedEventsCountA);
            Assert.Equal(2, notificationsA.Count);
            Assert.Equal(payload1, notificationsA[0].Payload);
            Assert.Equal(payload2, notificationsA[1].Payload);

            Assert.Equal(2, processedCountB);
            Assert.Equal(2, completedEventsCountB);
            Assert.Equal(2, notificationsB.Count);
            Assert.Equal(payload1, notificationsB[0].Payload);
            Assert.Equal(payload2, notificationsB[1].Payload);
        }
        finally
        {
            await handleA.UnsubscribeAsync();
            await handleB.UnsubscribeAsync();
        }
    }

    [Fact]
    public async Task OrleansClient_StateAndHistory_PersistForSameAgentIdAcrossCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = $"integration-direct-persist-{Guid.NewGuid():N}";

        var agent = _orleansClient.GetGrain<IAgent>(agentId);
        await agent.SetStateAsync("city", "Seattle", ct);
        var visit1 = await agent.IncrementAsync("visits", ct);
        await agent.AddHistoryAsync("user", "hello from direct persistence", ct);
        await agent.AddHistoryAsync("assistant", "response one", ct);

        var sameAgent = _orleansClient.GetGrain<IAgent>(agentId);
        var visit2 = await sameAgent.IncrementAsync("visits", ct);
        await sameAgent.AddHistoryAsync("user", "second direct persistence", ct);
        await sameAgent.AddHistoryAsync("assistant", "response two", ct);
        var state = await sameAgent.GetStateAsync(ct);
        var history = await sameAgent.GetHistoryAsync(ct);

        Assert.Equal(1, visit1);
        Assert.Equal(2, visit2);
        Assert.Equal("Seattle", state["city"]);
        Assert.Equal("2", state["visits"]);
        Assert.Equal(4, history.Count);
        Assert.Equal("user", history[0].Role);
        Assert.Equal("assistant", history[1].Role);
        Assert.Equal("user", history[2].Role);
        Assert.Equal("assistant", history[3].Role);
    }

    [Fact]
    public async Task OrleansClient_NotifyEnvelope_DeliversMetadataToSubscriber()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        var publisherId = $"integration-direct-envelope-publisher-{Guid.NewGuid():N}";
        var subscriberId = $"integration-direct-envelope-subscriber-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        var publisher = _orleansClient.GetGrain<IAgent>(publisherId);
        var subscriber = _orleansClient.GetGrain<IAgent>(subscriberId);

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
                ["source"] = "integration-tests",
                ["tenant"] = "alpha"
            }
        }, ct);

        List<NotificationRecord>? notifications = null;
        for (var i = 0; i < 80; i++)
        {
            notifications = await subscriber.GetNotificationsAsync(ct);
            if (notifications.Count >= 1)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
        }

        var entry = Assert.Single(notifications!);
        Assert.Equal(topic, entry.Topic);
        Assert.Equal("{\"city\":\"Seattle\",\"severity\":\"high\"}", entry.Payload);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("weather.alert", entry.Schema);
        Assert.Equal("1.0", entry.SchemaVersion);
        Assert.Equal(messageId, entry.MessageId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("integration-tests", entry.Headers["source"]);
        Assert.Equal("alpha", entry.Headers["tenant"]);
    }

    [Fact]
    public async Task OrleansClient_NotifyJsonHelper_DeliversTypedPayloadToSubscriber()
    {
        var ct = TestContext.Current.CancellationToken;
        const string topic = "weather.alert";
        var publisherId = $"integration-direct-json-publisher-{Guid.NewGuid():N}";
        var subscriberId = $"integration-direct-json-subscriber-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid().ToString("N");
        var correlationId = Guid.NewGuid().ToString("N");

        var publisher = _orleansClient.GetGrain<IAgent>(publisherId);
        var subscriber = _orleansClient.GetGrain<IAgent>(subscriberId);

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
                    ["source"] = "integration-tests-json",
                    ["tenant"] = "beta"
                }),
            ct);

        List<NotificationRecord>? notifications = null;
        for (var i = 0; i < 80; i++)
        {
            notifications = await subscriber.GetNotificationsAsync(ct);
            if (notifications.Count >= 1)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
        }

        var entry = Assert.Single(notifications!);
        var typedPayload = entry.ReadPayload<WeatherAlertPayload>();

        Assert.NotNull(typedPayload);
        Assert.Equal("Seattle", typedPayload!.City);
        Assert.Equal("critical", typedPayload.Severity);
        Assert.Equal(6, typedPayload.TemperatureC);
        Assert.Equal(topic, entry.Topic);
        Assert.Equal("application/json", entry.ContentType);
        Assert.Equal("weather.alert", entry.Schema);
        Assert.Equal("2.0", entry.SchemaVersion);
        Assert.Equal(messageId, entry.MessageId);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.Equal("integration-tests-json", entry.Headers["source"]);
        Assert.Equal("beta", entry.Headers["tenant"]);
    }

    private async Task<JsonElement> GetJsonAsync(string path, CancellationToken ct)
    {
        return await GetJsonAsync(_samplesClient, path, ct);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, ct);
        var payloadText = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"GET {path} failed with {(int)response.StatusCode} ({response.StatusCode}). Response: {payloadText}");

        using var document = JsonDocument.Parse(payloadText);
        return document.RootElement.Clone();
    }

    private static async Task<int> WaitForProcessedCountAsync(IAgent processor, int target, CancellationToken ct)
    {
        for (var i = 0; i < 80; i++)
        {
            var raw = await processor.GetStateValueAsync("processed-count", ct);
            var current = int.TryParse(raw, out var parsed) ? parsed : 0;
            if (current >= target)
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
        }

        throw new TimeoutException("Direct Orleans client event processing did not complete in time.");
    }

    private sealed record WeatherAlertPayload(string City, string Severity, int TemperatureC);
}
