# Testing

IAW provides two levels of testing: **unit tests** using Orleans `TestCluster` and **integration tests** using Aspire `DistributedApplicationTestingBuilder`. Both approaches test agents through the `IAgentV2` grain interface.

## Unit Tests with TestCluster

Unit tests spin up an in-process Orleans cluster with in-memory storage and streams. This is fast and requires no external dependencies.

### Project Setup

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

The silo configurator sets up in-memory grain storage, streaming, reminders, and the state machine storage provider required by `AgentV2`:

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
- `"PubSubStore"` is required by Orleans streaming infrastructure
- `"agents"` is the memory stream provider used by all IAW agent streams
- `VolatileStateMachineStorageProvider` + `AddStateMachineStorage()` provide the `IDurableDictionary` and `IDurableList` state that `AgentV2` requires

### Client Configurator

If your tests subscribe to streams from the client side:

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
using Core.V2;
using Orleans.TestingHost;
using Xunit;

public sealed class AgentV2BehaviorTests : IAsyncLifetime
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

### Testing Profile

```csharp
[Fact]
public async Task Profile_ReturnsExpectedValues()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("profile-1");

    var profile = await agent.GetProfileAsync(ct);

    Assert.Equal("profile-1", profile.Id);
    Assert.False(string.IsNullOrEmpty(profile.DisplayName));
}
```

### Testing Memory

```csharp
[Fact]
public async Task Memory_SetAndGet_Persists()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("memory-1");

    await agent.SetMemoryAsync("city", "Seattle", ct);
    var value = await agent.GetMemoryAsync("city", ct);

    Assert.Equal("Seattle", value);
}
```

### Testing Events

```csharp
[Fact]
public async Task Events_AreRecordedAndQueryable()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("events-1");

    await agent.AppendEventAsync(new AgentEvent { Type = "weather.refresh", Payload = "Seattle" }, ct);
    await agent.AppendEventAsync(new AgentEvent { Type = "weather.alert", Payload = "rain" }, ct);

    var events = await agent.QueryEventsAsync(ct: ct);

    Assert.Equal(2, events.Count);
    Assert.Equal("weather.refresh", events[0].Type);
    Assert.Equal("weather.alert", events[1].Type);
}
```

### Testing Notifications

```csharp
[Fact]
public async Task Notify_DeliversToSubscriber()
{
    var ct = TestContext.Current.CancellationToken;
    var publisher = _cluster.GrainFactory.GetGrain<IAgentV2>("pub-1");
    var subscriber = _cluster.GrainFactory.GetGrain<IAgentV2>("sub-1");

    await publisher.SubscribeAsync("weather.alert", "sub-1", ct);
    await publisher.NotifyAsync(new NotificationEnvelope
    {
        Topic = "weather.alert",
        Payload = "{\"city\":\"Seattle\"}",
        ContentType = "application/json",
        Schema = "weather.alert",
        SchemaVersion = "1.0"
    }, ct);

    var notifications = await subscriber.QueryNotificationsAsync(ct);
    var entry = Assert.Single(notifications);
    Assert.Equal("weather.alert", entry.Topic);
    Assert.Equal("application/json", entry.ContentType);
}
```

### Testing Stream Delivery

```csharp
[Fact]
public async Task StreamPublish_IsReceivedByClientSubscription()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("stream-1");

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

### Testing Scheduling

```csharp
[Fact]
public async Task Schedule_StartsAndStopsAtMax()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("schedule-1");

    await agent.StartScheduleAsync(TimeSpan.FromMilliseconds(40), 3, ct);

    for (var i = 0; i < 80; i++)
    {
        var status = await agent.GetScheduleStatusAsync(ct);
        if (!status.IsRunning)
        {
            Assert.Equal(3, status.TickCount);
            return;
        }
        await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
    }

    throw new TimeoutException("Schedule did not stop in time.");
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

### Test Class Structure

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core.V2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class AgentIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private HttpClient _samplesClient = null!;
    private IHost _orleansClientHost = null!;
    private IClusterClient _orleansClient = null!;

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
        var gatewayEndpoint = _app.GetEndpoint("samples", "orleans-gateway");

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

### Testing via Direct Orleans Client

```csharp
[Fact]
public async Task OrleansClient_MemoryAndMessages_PersistAcrossCalls()
{
    var ct = TestContext.Current.CancellationToken;
    var agentId = $"integration-{Guid.NewGuid():N}";

    var agent = _orleansClient.GetGrain<IAgentV2>(agentId);
    await agent.SetMemoryAsync("city", "Seattle", ct);
    await agent.AppendMessageAsync(new AgentMessage { Role = "user", Content = "hello" }, ct);

    var sameAgent = _orleansClient.GetGrain<IAgentV2>(agentId);
    var city = await sameAgent.GetMemoryAsync("city", ct);
    var messages = await sameAgent.QueryMessagesAsync(ct: ct);

    Assert.Equal("Seattle", city);
    Assert.Single(messages);
}
```

## Running Tests

```bash
# Run all tests
dotnet test IAW.slnx

# Run unit tests only
dotnet test test/Core.Tests/IAW.Core.Tests.csproj

# Run integration tests only
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj

# Run with verbose output
dotnet test IAW.slnx --verbosity normal
```

::: warning Integration Test Requirements
Integration tests start the full Aspire AppHost, which requires Docker to be running for any container resources. The `DistributedApplicationTestingBuilder` spins up the application and waits for resources to become healthy before running tests.
:::
