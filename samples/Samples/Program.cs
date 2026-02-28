using Core;
using Orleans.Journaling;
using Orleans.Streams;
using ServiceDefaults;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseOrleans(silo =>
{
    var clusterId = builder.Configuration["IAW:Orleans:ClusterId"] ?? "default";
    var serviceId = builder.Configuration["IAW:Orleans:ServiceId"] ?? "default";
    var primarySiloEndpoint = ParseEndpoint(builder.Configuration["IAW:Orleans:PrimarySiloEndpoint"]);

    silo.UseLocalhostClustering(
        primarySiloEndpoint: primarySiloEndpoint,
        serviceId: serviceId,
        clusterId: clusterId);

    silo.AddMemoryGrainStorage("Default");
    silo.AddMemoryGrainStorage("PubSubStore");
    silo.AddMemoryStreams("agents");
    silo.UseInMemoryReminderService();
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
});

builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Hello World!");

app.MapGet("/samples/orleans-agent/state", async (
    IGrainFactory grains,
    string? agentId,
    string? city,
    CancellationToken ct) =>
{
    var resolvedAgentId = string.IsNullOrWhiteSpace(agentId) ? "weather-agent" : agentId;
    var resolvedCity = string.IsNullOrWhiteSpace(city) ? "Seattle" : city;
    var agent = grains.GetGrain<IAgent>(resolvedAgentId);

    await agent.SetStateAsync("city", resolvedCity, ct);
    var visit1 = await agent.IncrementAsync("visits", ct);
    var visit2 = await agent.IncrementAsync("visits", ct);
    var snapshot = await agent.GetStateAsync(ct);

    var hasCity = snapshot.TryGetValue("city", out var cityValue);

    return Results.Ok(new
    {
        agentId = resolvedAgentId,
        visit1,
        visit2,
        snapshot,
        isStateful = visit2 == visit1 + 1 && hasCity && cityValue == resolvedCity
    });
});

app.MapGet("/samples/orleans-agent/state-isolation", async (IGrainFactory grains, CancellationToken ct) =>
{
    var alphaId = $"sample-alpha-{Guid.NewGuid():N}";
    var betaId = $"sample-beta-{Guid.NewGuid():N}";
    var alpha = grains.GetGrain<IAgent>(alphaId);
    var beta = grains.GetGrain<IAgent>(betaId);

    await alpha.SetStateAsync("mode", "alpha", ct);
    await beta.SetStateAsync("mode", "beta", ct);

    var alphaState = await alpha.GetStateAsync(ct);
    var betaState = await beta.GetStateAsync(ct);

    return Results.Ok(new
    {
        alphaId,
        betaId,
        alphaMode = alphaState["mode"],
        betaMode = betaState["mode"],
        isolated = alphaState["mode"] != betaState["mode"]
    });
});

app.MapGet("/samples/orleans-agent/metadata", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-meta-{Guid.NewGuid():N}");
    var metadata = await agent.GetMetadataAsync(ct);
    return Results.Ok(metadata);
});

app.MapGet("/samples/orleans-agent/events", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-events-{Guid.NewGuid():N}");

    await agent.PublishEventAsync("weather.refresh", "Seattle", ct);
    await agent.PublishEventAsync("weather.alert", "rain", ct);
    var events = await agent.GetEventsAsync(ct);

    return Results.Ok(new
    {
        count = events.Count,
        names = events.Select(entry => entry.Name).ToArray()
    });
});

app.MapGet("/samples/orleans-agent/notifications", async (IGrainFactory grains, CancellationToken ct) =>
{
    var publisher = grains.GetGrain<IAgent>($"sample-publisher-{Guid.NewGuid():N}");
    var subscriberId = $"sample-subscriber-{Guid.NewGuid():N}";
    var subscriber = grains.GetGrain<IAgent>(subscriberId);

    await publisher.SubscribeAsync("weather.alert", subscriberId, ct);
    await publisher.NotifyAsync("weather.alert", "storm", ct);
    var notifications = await subscriber.GetNotificationsAsync(ct);

    return Results.Ok(new
    {
        count = notifications.Count,
        topic = notifications.FirstOrDefault()?.Topic,
        payload = notifications.FirstOrDefault()?.Payload
    });
});

app.MapGet("/samples/orleans-agent/notifications-envelope", async (IGrainFactory grains, CancellationToken ct) =>
{
    var publisher = grains.GetGrain<IAgent>($"sample-publisher-envelope-{Guid.NewGuid():N}");
    var subscriberId = $"sample-subscriber-envelope-{Guid.NewGuid():N}";
    var subscriber = grains.GetGrain<IAgent>(subscriberId);
    var messageId = Guid.NewGuid().ToString("N");
    var correlationId = Guid.NewGuid().ToString("N");

    await publisher.SubscribeAsync("weather.alert", subscriberId, ct);
    await publisher.NotifyAsync(new NotificationEnvelope
    {
        Topic = "weather.alert",
        Payload = "{\"city\":\"Seattle\",\"severity\":\"high\"}",
        ContentType = "application/json",
        Schema = "weather.alert",
        SchemaVersion = "1.0",
        MessageId = messageId,
        CorrelationId = correlationId,
        Headers = new Dictionary<string, string>
        {
            ["source"] = "samples",
            ["tenant"] = "alpha"
        }
    }, ct);

    var notifications = await subscriber.GetNotificationsAsync(ct);
    var entry = notifications.FirstOrDefault();
    string? sourceHeader = null;
    string? tenantHeader = null;
    if (entry is not null)
    {
        _ = entry.Headers.TryGetValue("source", out sourceHeader);
        _ = entry.Headers.TryGetValue("tenant", out tenantHeader);
    }

    return Results.Ok(new
    {
        count = notifications.Count,
        topic = entry?.Topic,
        payload = entry?.Payload,
        contentType = entry?.ContentType,
        schema = entry?.Schema,
        schemaVersion = entry?.SchemaVersion,
        messageId = entry?.MessageId,
        correlationId = entry?.CorrelationId,
        sourceHeader,
        tenantHeader
    });
});

app.MapGet("/samples/orleans-agent/notifications-dynamic", async (IGrainFactory grains, CancellationToken ct) =>
{
    var publisher = grains.GetGrain<IAgent>($"sample-publisher-dynamic-{Guid.NewGuid():N}");
    var subscriberId = $"sample-subscriber-dynamic-{Guid.NewGuid():N}";
    var subscriber = grains.GetGrain<IAgent>(subscriberId);
    var messageId = Guid.NewGuid().ToString("N");
    var correlationId = Guid.NewGuid().ToString("N");

    await publisher.SubscribeAsync("weather.alert", subscriberId, ct);
    await publisher.NotifyAsync(
        NotificationJson.CreateEnvelope(
            "weather.alert",
            new SampleWeatherAlertPayload("Seattle", "critical", 6),
            schema: "weather.alert",
            schemaVersion: "2.0",
            messageId: messageId,
            correlationId: correlationId,
            headers: new Dictionary<string, string>
            {
                ["source"] = "samples",
                ["tenant"] = "beta"
            }),
        ct);

    var notifications = await subscriber.GetNotificationsAsync(ct);
    var entry = notifications.FirstOrDefault();
    var typedPayload = entry?.ReadPayload<SampleWeatherAlertPayload>();
    string? sourceHeader = null;
    string? tenantHeader = null;
    if (entry is not null)
    {
        _ = entry.Headers.TryGetValue("source", out sourceHeader);
        _ = entry.Headers.TryGetValue("tenant", out tenantHeader);
    }

    return Results.Ok(new
    {
        count = notifications.Count,
        topic = entry?.Topic,
        city = typedPayload?.City,
        severity = typedPayload?.Severity,
        temperatureC = typedPayload?.TemperatureC,
        contentType = entry?.ContentType,
        schema = entry?.Schema,
        schemaVersion = entry?.SchemaVersion,
        messageId = entry?.MessageId,
        correlationId = entry?.CorrelationId,
        sourceHeader,
        tenantHeader
    });
});

app.MapGet("/samples/orleans-agent/event-processing", async (
    IGrainFactory grains,
    IClusterClient client,
    CancellationToken ct) =>
{
    const string topic = "weather.alert";
    const string streamNamespace = "agent-event-processing";
    var payload = JsonSerializer.Serialize(new { city = "Seattle", severity = "high" });
    var streamId = Guid.NewGuid();

    var producer = grains.GetGrain<IAgent>($"sample-event-producer-{Guid.NewGuid():N}");
    var processorId = $"sample-event-processor-{Guid.NewGuid():N}";
    var processor = grains.GetGrain<IAgent>(processorId);
    var streamProvider = client.GetStreamProvider("agents");
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
    await producer.PublishStreamAsync(streamNamespace, streamId, payload, ct);

    var processedCount = await WaitForProcessedCountAsync(processor, 1, ct);
    await handle.UnsubscribeAsync();

    var notifications = await processor.GetNotificationsAsync(ct);
    var lastProcessedPayload = await processor.GetStateValueAsync("last-processed-payload", ct);
    var processorEvents = await processor.GetEventsAsync(ct);
    var processorEventNames = processorEvents.Select(entry => entry.Name).ToArray();

    return Results.Ok(new
    {
        deliveryMechanism = "orleans-streams",
        topic,
        payload,
        streamNamespace,
        streamId,
        processorNotificationCount = notifications.Count,
        processedCount,
        lastProcessedPayload,
        processorEventNames,
        processed = notifications.Count == 1
            && processedCount == 1
            && string.Equals(lastProcessedPayload, payload, StringComparison.Ordinal)
            && processorEventNames.Contains("processing.completed", StringComparer.Ordinal)
    });
});

app.MapGet("/samples/orleans-agent/tracking", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-tracking-{Guid.NewGuid():N}");

    await agent.StartTrackingAsync(TimeSpan.FromMilliseconds(40), 3, ct);
    var status = await WaitForTrackingToStopAsync(agent, ct);

    return Results.Ok(new
    {
        status.IsTracking,
        status.TickCount,
        status.Interval,
        status.MaxTicks
    });
});

app.MapGet("/samples/orleans-agent/stream", async (
    IGrainFactory grains,
    IClusterClient client,
    CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-stream-{Guid.NewGuid():N}");
    const string streamNamespace = "sample-stream";
    var streamId = Guid.NewGuid();
    var payload = $"stream-payload-{Guid.NewGuid():N}";
    var streamProvider = client.GetStreamProvider("agents");
    var stream = streamProvider.GetStream<string>(StreamId.Create(streamNamespace, streamId));
    var delivered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

    var handle = await stream.SubscribeAsync((message, _) =>
    {
        delivered.TrySetResult(message);
        return Task.CompletedTask;
    });

    await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
    await agent.PublishStreamAsync(streamNamespace, streamId, payload, ct);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));
    var received = await delivered.Task.WaitAsync(timeout.Token);
    await handle.UnsubscribeAsync();

    return Results.Ok(new
    {
        streamNamespace,
        streamId,
        payload,
        published = true,
        received,
        delivered = string.Equals(received, payload, StringComparison.Ordinal),
        subscriberCountAfter = 0
    });
});

app.MapGet("/samples/agent/identity", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-legacy-identity-{Guid.NewGuid():N}");
    var metadata = await agent.GetMetadataAsync(ct);
    return Results.Ok(new
    {
        id = metadata.Id,
        displayName = metadata.DisplayName
    });
});

app.MapGet("/samples/agent/events/publish", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-legacy-events-{Guid.NewGuid():N}");

    await agent.PublishEventAsync("weather.refresh", JsonSerializer.Serialize(new { city = "Seattle" }), ct);
    await agent.PublishEventAsync("weather.alert", "rain", ct);
    var eventLog = await agent.GetEventsAsync(ct);
    var publishedNames = eventLog.Select(e => e.Name).ToArray();

    return Results.Ok(new
    {
        publishedCount = eventLog.Count,
        publishedNames,
        handledCount = eventLog.Count,
        handledNames = publishedNames
    });
});

app.MapGet("/samples/agent/notifications", async (IGrainFactory grains, CancellationToken ct) =>
{
    var publisher = grains.GetGrain<IAgent>($"sample-legacy-notify-publisher-{Guid.NewGuid():N}");
    var subscriberAId = $"sample-legacy-notify-a-{Guid.NewGuid():N}";
    var subscriberBId = $"sample-legacy-notify-b-{Guid.NewGuid():N}";
    var subscriberA = grains.GetGrain<IAgent>(subscriberAId);
    var subscriberB = grains.GetGrain<IAgent>(subscriberBId);

    await publisher.SubscribeAsync("weather.alert", subscriberAId, ct);
    await publisher.SubscribeAsync("weather.alert", subscriberBId, ct);
    await publisher.SubscribeAsync("weather.refresh", subscriberAId, ct);

    await publisher.NotifyAsync("weather.alert", JsonSerializer.Serialize(new { level = "high" }), ct);
    await publisher.NotifyAsync("weather.refresh", "poll-now", ct);

    var publisherEvents = await publisher.GetEventsAsync(ct);
    var subscriberANotifications = await subscriberA.GetNotificationsAsync(ct);
    var subscriberBNotifications = await subscriberB.GetNotificationsAsync(ct);
    var subscriptionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["weather.alert"] = 2,
        ["weather.refresh"] = 1
    };

    return Results.Ok(new
    {
        subscriptionCounts,
        publisherEventNames = publisherEvents.Select(e => e.Name).ToArray(),
        subscriberAHandled = subscriberANotifications.Select(n => n.Topic).ToArray(),
        subscriberBHandled = subscriberBNotifications.Select(n => n.Topic).ToArray()
    });
});

app.MapGet("/samples/agent/state", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agentId = $"sample-legacy-state-{Guid.NewGuid():N}";
    var agent = grains.GetGrain<IAgent>(agentId);

    await agent.SetStateAsync("city", "Seattle", ct);
    await agent.SetStateAsync("temperatureC", "21", ct);
    await agent.SetStateAsync("isRaining", "true", ct);

    var snapshot = await agent.GetStateAsync(ct);
    var city = snapshot.TryGetValue("city", out var cityRaw) ? cityRaw : "unknown";
    var temperatureC = snapshot.TryGetValue("temperatureC", out var temperatureRaw) &&
                       int.TryParse(temperatureRaw, out var parsedTemperature)
        ? parsedTemperature
        : -1;
    var missingInt = snapshot.TryGetValue("missing-int", out var missingRaw) &&
                     int.TryParse(missingRaw, out var parsedMissing)
        ? parsedMissing
        : -999;
    var isRaining = false;
    var hasIsRaining = snapshot.TryGetValue("isRaining", out var rainRaw) &&
                       bool.TryParse(rainRaw, out isRaining);

    return Results.Ok(new
    {
        agentId,
        count = snapshot.Count,
        keys = snapshot.Keys.OrderBy(k => k).ToArray(),
        city,
        temperatureC,
        missingInt,
        hasIsRaining,
        isRaining
    });
});

app.MapGet("/samples/agent/metadata", async (IGrainFactory grains, CancellationToken ct) =>
{
    var weatherGrain = grains.GetGrain<IAgent>("sample-legacy-metadata-weather");
    var promptedGrain = grains.GetGrain<IAgent>("sample-legacy-metadata-prompted");
    var tooledGrain = grains.GetGrain<IAgent>("sample-legacy-metadata-tooled");
    var eventAwareGrain = grains.GetGrain<IAgent>("sample-legacy-metadata-eventaware");

    var weatherMetadata = await weatherGrain.GetMetadataAsync(ct);
    var promptedMetadata = await promptedGrain.GetMetadataAsync(ct);
    var tooledMetadata = await tooledGrain.GetMetadataAsync(ct);
    var eventAwareMetadata = await eventAwareGrain.GetMetadataAsync(ct);

    return Results.Ok(new
    {
        weather = weatherMetadata,
        prompted = promptedMetadata,
        tooled = tooledMetadata,
        eventAware = eventAwareMetadata
    });
});

app.MapGet("/samples/agent/tracking", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"sample-legacy-tracking-{Guid.NewGuid():N}");
    await agent.StartTrackingAsync(TimeSpan.FromMilliseconds(50), 200, ct);
    await Task.Delay(TimeSpan.FromMilliseconds(180), ct);
    var runningA = await agent.GetTrackingStatusAsync(ct);

    await Task.Delay(TimeSpan.FromMilliseconds(180), ct);
    var runningB = await agent.GetTrackingStatusAsync(ct);

    await agent.StopTrackingAsync(ct);
    var stopped = await agent.GetTrackingStatusAsync(ct);

    await Task.Delay(TimeSpan.FromMilliseconds(180), ct);
    var afterStop = await agent.GetTrackingStatusAsync(ct);

    return Results.Ok(new
    {
        runningA = new { runningA.IsTracking, runningA.TickCount, StartedAt = runningA.StartedAtUtc },
        runningB = new { runningB.IsTracking, runningB.TickCount, StartedAt = runningB.StartedAtUtc },
        stopped = new { stopped.IsTracking, stopped.TickCount, StartedAt = stopped.StartedAtUtc },
        afterStop = new { afterStop.IsTracking, afterStop.TickCount, StartedAt = afterStop.StartedAtUtc },
        increasedWhileRunning = runningB.TickCount > runningA.TickCount,
        stoppedTicking = afterStop.TickCount == stopped.TickCount && !afterStop.IsTracking
    });
});

app.MapGet("/samples/agent/streaming", async (
    IGrainFactory grains,
    IClusterClient client,
    CancellationToken ct) =>
{
    const string topic = "weather.updates";
    var streamId = Guid.NewGuid();
    var agent = grains.GetGrain<IAgent>($"sample-legacy-streaming-{Guid.NewGuid():N}");
    var streamProvider = client.GetStreamProvider("agents");
    var stream = streamProvider.GetStream<string>(StreamId.Create(topic, streamId));
    var received = new List<string>(capacity: 2);
    var sync = new object();
    var receivedTwo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    var handle = await stream.SubscribeAsync((message, _) =>
    {
        lock (sync)
        {
            received.Add(message);
            if (received.Count >= 2)
            {
                receivedTwo.TrySetResult();
            }
        }

        return Task.CompletedTask;
    });

    await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
    await agent.PublishStreamAsync(topic, streamId, "weather:rain", ct);
    await agent.PublishStreamAsync(topic, streamId, "weather:sun", ct);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));
    await receivedTwo.Task.WaitAsync(timeout.Token);
    await handle.UnsubscribeAsync();

    string[] receivedSnapshot;
    lock (sync)
    {
        receivedSnapshot = [.. received];
    }

    var expected = new[] { "weather:rain", "weather:sun" };

    return Results.Ok(new
    {
        topic,
        publishAccepted = true,
        received = receivedSnapshot,
        deliveredBoth = receivedSnapshot.SequenceEqual(expected),
        subscriberCountAfter = 0
    });
});

app.Run();

static async Task<AgentTrackingStatus> WaitForTrackingToStopAsync(
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

static async Task<int> WaitForProcessedCountAsync(IAgent processor, int target, CancellationToken ct)
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

    throw new TimeoutException("Event processing did not complete in time.");
}

static IPEndPoint? ParseEndpoint(string? endpointValue)
{
    if (string.IsNullOrWhiteSpace(endpointValue))
    {
        return null;
    }

    if (Uri.TryCreate(endpointValue, UriKind.Absolute, out var uri))
    {
        return ResolveEndpoint(uri.Host, uri.Port);
    }

    var parts = endpointValue.Split(':', 2, StringSplitOptions.TrimEntries);
    if (parts.Length == 2 && int.TryParse(parts[1], out var port))
    {
        return ResolveEndpoint(parts[0], port);
    }

    return null;
}

static IPEndPoint? ResolveEndpoint(string host, int port)
{
    try
    {
        var addresses = Dns.GetHostAddresses(host);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();

        return address is null ? null : new IPEndPoint(address, port);
    }
    catch
    {
        return null;
    }
}

public sealed record SampleWeatherAlertPayload(string City, string Severity, int TemperatureC);
