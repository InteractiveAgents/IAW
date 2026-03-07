# Testing

IAW provides testing infrastructure for V3 agents using Orleans `TestCluster` and Aspire `DistributedApplicationTestingBuilder`.

## Unit Tests with TestCluster

Unit tests spin up an in-process Orleans cluster with in-memory storage and streams. Fast and requires no external dependencies.

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

The silo configurator sets up in-memory grain storage, streaming, reminders, and the state machine storage provider required by the V3 `Agent`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;

public sealed class AgentSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams("agents")
            .UseInMemoryReminderService();

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider,
            VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();
    }
}
```

Key points:
- `"Default"` storage is required for general grain persistence
- `"PubSubStore"` is required by Orleans streaming infrastructure
- `"agents"` is the memory stream provider used by all V3 agent streams
- `VolatileStateMachineStorageProvider` + `AddStateMachineStorage()` provide the `IDurableDictionary` and `IDurableList` state that the `Agent` base class requires

### MockChatClient

For testing without a real LLM, create a mock `IChatClient`:

```csharp
using Microsoft.Extensions.AI;

public sealed class MockChatClient : IChatClient
{
    public string ResponseText { get; set; } = "Mock response";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText));
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<ChatResponseUpdate>();
    }

    public void Dispose() { }

    public ChatClientMetadata Metadata => new("mock");
}
```

Register it in the silo configurator:

```csharp
siloBuilder.Services.AddSingleton<IChatClient>(new MockChatClient());
```

### Test Class Structure

Tests use `IAsyncLifetime` (xUnit v3) to manage the test cluster lifecycle:

```csharp
using Core.V3;
using Orleans.TestingHost;
using Xunit;

public sealed class AgentBehaviorTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<AgentSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.DisposeAsync();
    }
}
```

### Testing Conversation

```csharp
[Fact]
public async Task GetResponse_ReturnsText()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("conv-1");

    var response = await agent.GetResponse("Hello!", ct);

    Assert.False(string.IsNullOrEmpty(response));
}
```

### Testing Metadata

```csharp
[Fact]
public async Task Metadata_ReturnsAgentInfo()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("meta-1");

    var metadata = await agent.GetMetadataAsync(ct);

    Assert.False(string.IsNullOrEmpty(metadata.DisplayName));
    Assert.False(string.IsNullOrEmpty(metadata.Description));
}
```

### Testing State

```csharp
[Fact]
public async Task SetWorkspace_PersistsInState()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("state-1");

    await agent.SetWorkspaceAsync("/test/workspace", ct);
    var state = await agent.GetStateAsync(ct);

    Assert.True(state.Entries.ContainsKey("workspace-path"));
}
```

### Testing Events

```csharp
[Fact]
public async Task HandleEvent_RecordsInLog()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("events-1");

    var evt = new AgentEvent(
        "test.event", "source-1", Guid.NewGuid().ToString(),
        DateTimeOffset.UtcNow, new Dictionary<string, object> { ["key"] = "value" });

    await agent.HandleEventAsync(evt, ct);
    var log = await agent.GetEventLogAsync(ct);

    // Log may or may not contain the event depending on HandleEventAsync implementation
    Assert.NotNull(log);
}
```

### Testing Capabilities

```csharp
[Fact]
public async Task Capabilities_ReportsCorrectly()
{
    var ct = TestContext.Current.CancellationToken;
    var agent = _cluster.GrainFactory.GetGrain<IAgent>("caps-1");

    var caps = await agent.GetCapabilitiesAsync(ct);

    Assert.True(caps.HasMemory);
    Assert.True(caps.IsCancellable);
}
```

### Testing Stream Subscriptions

```csharp
[Fact]
public async Task ActiveSubscriptions_ReflectsInterfaces()
{
    var ct = TestContext.Current.CancellationToken;
    // Use a grain that implements IStreamConsumer<T>
    var agent = _cluster.GrainFactory.GetGrain<ICodeReviewAgent>("review-1");

    var subs = await agent.GetActiveSubscriptionsAsync(ct);

    Assert.Contains("code.changed", subs);
}
```

## Integration Tests with Aspire

Integration tests run the full Aspire AppHost and test against live endpoints and a real Orleans cluster.

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
using Core.V3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class AgentIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
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
        await _orleansClientHost.StopAsync();
        _orleansClientHost.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ReturnsMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _orleansClient.GetGrain<IAgent>("integration-test");
        var metadata = await agent.GetMetadataAsync(ct);
        Assert.NotNull(metadata);
    }
}
```

::: warning Integration Test Requirements
Integration tests start the full Aspire AppHost, which requires Docker to be running for any container resources. The `DistributedApplicationTestingBuilder` spins up the application and waits for resources to become healthy before running tests.
:::

## Running Tests

```bash
# Run all tests
dotnet test IAW.slnx

# Run unit tests only
dotnet test test/Core.Tests/IAW.Core.Tests.csproj

# Run integration tests only
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj

# Run a single test
dotnet test IAW.slnx --filter "FullyQualifiedName~GetResponse_ReturnsText"
```
