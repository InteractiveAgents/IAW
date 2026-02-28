# Testing

IAW provides two levels of testing: **unit tests** using Orleans `TestCluster` and **integration tests** using Aspire `DistributedApplicationTestingBuilder`. Both approaches test agents through the `IAgent` grain interface.

## Unit Tests with TestCluster

Unit tests spin up an in-process Orleans cluster with in-memory storage and streams. This is fast and requires no external dependencies.

### Project Setup

Create a test project and add these package references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Core\Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Orleans.Persistence.Memory" />
    <PackageReference Include="Microsoft.Orleans.Reminders" />
    <PackageReference Include="Microsoft.Orleans.Streaming" />
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

### Silo Configurator

The silo configurator sets up in-memory grain storage, streaming, reminders, and the state machine storage provider required by the `Agent` base class:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

public sealed class AgentsSiloConfigurator : ISiloConfigurator
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
```

Key points:
- `"Default"` storage is required for general grain persistence
- `"PubSubStore"` is required by the Orleans streaming infrastructure
- `"agents"` is the memory stream provider name used by all IAW agent streams
- `VolatileStateMachineStorageProvider` + `AddStateMachineStorage()` provide the `IDurableDictionary` and `IDurableList` state the `Agent` constructor requires

### Client Configurator

If your tests subscribe to streams from the client side, you also need a client configurator:

```csharp
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;

public sealed class AgentsClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("agents");
    }
}
```

### Test Class Structure

Tests use `IAsyncLifetime` (xUnit v3) to manage the test cluster lifecycle:

```csharp
using Core;
using Orleans.TestingHost;
using Xunit;

public sealed class AgentBehaviorTests : IAsyncLifetime
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
}
```

### Writing Tests Against IAgent

Get an agent grain from the cluster and call its methods:

```csharp
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
```

### Testing State and Counters

```csharp
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
```

### Testing Events

```csharp
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
```

### Testing Notifications with Envelope

```csharp
[Fact]
public async Task Notify_WithEnvelope_DeliversMetadataToSubscribers()
{
    var ct = TestContext.Current.CancellationToken;
    var publisher = _cluster.GrainFactory.GetGrain<IAgent>("publisher-envelope-1");
    var subscriber = _cluster.GrainFactory.GetGrain<IAgent>("subscriber-envelope-1");

    await publisher.SubscribeAsync("weather.alert", "subscriber-envelope-1", ct);
    await publisher.NotifyAsync(new NotificationEnvelope
    {
        Topic = "weather.alert",
        Payload = "{\"city\":\"Seattle\",\"severity\":\"high\"}",
        ContentType = "application/json",
        Schema = "weather.alert",
        SchemaVersion = "1.0",
        MessageId = Guid.NewGuid().ToString("N"),
        CorrelationId = Guid.NewGuid().ToString("N"),
        Headers = new Dictionary<string, string>
        {
            ["source"] = "agents-tests",
            ["tenant"] = "alpha"
        }
    }, ct);

    var notifications = await subscriber.GetNotificationsAsync(ct);
    var entry = Assert.Single(notifications);
    Assert.Equal("weather.alert", entry.Topic);
    Assert.Equal("application/json", entry.ContentType);
}
```

### Testing Typed Notifications with NotificationJson

```csharp
[Fact]
public async Task Notify_WithJsonHelper_DeliversTypedPayloadToSubscriber()
{
    var ct = TestContext.Current.CancellationToken;
    var publisher = _cluster.GrainFactory.GetGrain<IAgent>("publisher-json-1");
    var subscriber = _cluster.GrainFactory.GetGrain<IAgent>("subscriber-json-1");

    await publisher.SubscribeAsync("weather.alert", "subscriber-json-1", ct);
    await publisher.NotifyAsync(
        NotificationJson.CreateEnvelope(
            "weather.alert",
            new WeatherAlertPayload("Seattle", "critical", 6),
            schema: "weather.alert",
            schemaVersion: "2.0"),
        ct);

    var notifications = await subscriber.GetNotificationsAsync(ct);
    var entry = Assert.Single(notifications);
    var typedPayload = entry.ReadPayload<WeatherAlertPayload>();

    Assert.NotNull(typedPayload);
    Assert.Equal("Seattle", typedPayload!.City);
    Assert.Equal("critical", typedPayload.Severity);
    Assert.Equal(6, typedPayload.TemperatureC);
}

private sealed record WeatherAlertPayload(string City, string Severity, int TemperatureC);
```

### Testing Stream Delivery

```csharp
[Fact]
public async Task StreamPublish_IsReceivedByClientSubscription()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("stream-1");

    var streamProvider = _cluster.Client.GetStreamProvider("agents");
    var streamGuid = Guid.NewGuid();
    var streamId = StreamId.Create("agent-tests", streamGuid);
    var stream = streamProvider.GetStream<string>(streamId);
    var received = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);

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
```

### Testing Tracking

```csharp
[Fact]
public async Task Tracking_StartsTicks_AndStopsAtMax()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("tracking-1");

    await agent.StartTrackingAsync(TimeSpan.FromMilliseconds(40), 3, ct);

    // Poll until tracking stops
    for (var i = 0; i < 80; i++)
    {
        var status = await agent.GetTrackingStatusAsync(ct);
        if (!status.IsTracking)
        {
            Assert.Equal(3, status.TickCount);
            return;
        }
        await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
    }

    throw new TimeoutException("Tracking did not stop in time.");
}
```

## Integration Tests with Aspire

Integration tests run the full Aspire AppHost and test against live HTTP endpoints and a real Orleans cluster.

### Project Setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Core\Core.csproj" />
    <ProjectReference Include="..\..\src\IAW.AppHost\Aspire.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

The `ProjectReference` to the AppHost project is required so `DistributedApplicationTestingBuilder` can discover and start the application.

### Test Class Structure

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class AgentIntegrationTests : IAsyncLifetime
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
}
```

Key points:
- `DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>` starts the full AppHost
- Command-line arguments pass secret parameters (e.g. `--Parameters:anthropic-api-key=test-key`)
- `WaitForResourceAsync` ensures the sample service is running before tests execute
- `CreateHttpClient` gives you an HTTP client pointed at the named resource
- `GetEndpoint` retrieves the Orleans gateway URI so you can create a direct `IClusterClient`

### Testing via HTTP Endpoints

```csharp
[Fact]
public async Task SampleEndpoints_ReportExpectedBehavior()
{
    var ct = TestContext.Current.CancellationToken;

    var response = await _samplesClient.GetAsync("/samples/orleans-agent/metadata", ct);
    response.EnsureSuccessStatusCode();

    var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    var capabilities = json.RootElement
        .GetProperty("capabilities")
        .EnumerateArray()
        .Select(item => item.GetString())
        .ToArray();

    Assert.Contains("state", capabilities);
    Assert.Contains("streams", capabilities);
}
```

### Testing via Direct Orleans Client

```csharp
[Fact]
public async Task OrleansClient_StateAndHistory_PersistAcrossCalls()
{
    var ct = TestContext.Current.CancellationToken;
    var agentId = $"integration-{Guid.NewGuid():N}";

    var agent = _orleansClient.GetGrain<IAgent>(agentId);
    await agent.SetStateAsync("city", "Seattle", ct);
    var visit1 = await agent.IncrementAsync("visits", ct);
    await agent.AddHistoryAsync("user", "hello", ct);

    var sameAgent = _orleansClient.GetGrain<IAgent>(agentId);
    var visit2 = await sameAgent.IncrementAsync("visits", ct);
    var state = await sameAgent.GetStateAsync(ct);
    var history = await sameAgent.GetHistoryAsync(ct);

    Assert.Equal(1, visit1);
    Assert.Equal(2, visit2);
    Assert.Equal("Seattle", state["city"]);
    Assert.Equal(1, history.Count);
}
```

### Testing Stream Event Processing End-to-End

```csharp
[Fact]
public async Task OrleansClient_StreamEventProcessing_CompletesEndToEnd()
{
    var ct = TestContext.Current.CancellationToken;
    const string topic = "weather.alert";
    var streamId = Guid.NewGuid();
    var payload = JsonSerializer.Serialize(
        new { city = "Seattle", severity = "high" });

    var processor = _orleansClient.GetGrain<IAgent>(
        $"processor-{Guid.NewGuid():N}");
    var streamProvider = _orleansClient.GetStreamProvider("agents");
    var stream = streamProvider.GetStream<string>(
        StreamId.Create("agent-event-processing", streamId));

    var handle = await stream.SubscribeAsync(async (message, _) =>
    {
        await processor.ReceiveNotificationAsync(topic, message, ct);
        await processor.IncrementAsync("processed-count", ct);
        await processor.PublishEventAsync("processing.completed", message, ct);
    });

    await Task.Delay(TimeSpan.FromMilliseconds(40), ct);
    await stream.OnNextAsync(payload);

    // Wait for processing to complete
    for (var i = 0; i < 80; i++)
    {
        var raw = await processor.GetStateValueAsync("processed-count", ct);
        if (int.TryParse(raw, out var count) && count >= 1) break;
        await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
    }

    var notifications = await processor.GetNotificationsAsync(ct);
    var events = await processor.GetEventsAsync(ct);
    await handle.UnsubscribeAsync();

    Assert.Single(notifications);
    Assert.Contains(events,
        e => e.Name == "processing.completed");
}
```

## Running Tests

```bash
# Run all tests
dotnet test IAW.slnx

# Run unit tests only
dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj

# Run integration tests only
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj

# Run with verbose output
dotnet test IAW.slnx --verbosity normal
```

::: warning Integration Test Requirements
Integration tests start the full Aspire AppHost, which requires Docker to be running for any container resources. The `DistributedApplicationTestingBuilder` spins up the application and waits for resources to become healthy before running tests.
:::
