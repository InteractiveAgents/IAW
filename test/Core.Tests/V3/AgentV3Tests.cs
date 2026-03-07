using global::Core.V3;
using IAW.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.V3;

public sealed class V3SiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams("agents")
            .UseInMemoryReminderService();

        siloBuilder.Services.AddSingleton<Orleans.Journaling.IStateMachineStorageProvider,
            Orleans.Journaling.VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        siloBuilder.Services.AddSingleton<IChatClient>(new MockChatClient().ReturnsText("mock-response"));
    }
}

public sealed class V3ClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(Microsoft.Extensions.Configuration.IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("agents");
    }
}

public class AgentV3Tests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<V3SiloConfigurator>();
        builder.AddClientBuilderConfigurator<V3ClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private global::Core.V3.IAgent Agent(string id) => _cluster.GrainFactory.GetGrain<ITestAgentV3>(id);
    private string UniqueId(string prefix) => $"{prefix}-{_testRunId}";

    [Fact]
    public async Task GetResponse_ReturnsNonEmpty()
    {
        var agent = Agent(UniqueId("resp"));
        var response = await agent.GetResponse("Hello", CancellationToken.None);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetHistory_AfterResponse_HasEntries()
    {
        var agent = Agent(UniqueId("hist"));
        await agent.GetResponse("Hello", CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.True(history.Count > 0);
    }

    [Fact]
    public async Task ClearHistory_EmptiesMessages()
    {
        var agent = Agent(UniqueId("clear"));
        await agent.GetResponse("Hello", CancellationToken.None);
        await agent.ClearHistoryAsync(CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.Empty(history);
    }

    [Fact]
    public async Task SetWorkspace_PersistsInState()
    {
        var agent = Agent(UniqueId("ws"));
        await agent.SetWorkspaceAsync("/tmp/test", CancellationToken.None);
        var agentState = await agent.GetStateAsync(CancellationToken.None);
        Assert.True(agentState.Entries.ContainsKey("workspace-path"));
        Assert.Equal("/tmp/test", agentState.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task GetMetadata_ReturnsAgentInfo()
    {
        var agent = Agent(UniqueId("meta"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.Equal("TestAgentV3", metadata.AgentType);
        Assert.Equal("Test Agent V3", metadata.DisplayName);
        Assert.Equal(AgentKind.Static, metadata.Kind);
    }

    [Fact]
    public async Task GetCapabilities_ReturnsDefaults()
    {
        var agent = Agent(UniqueId("caps"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasMemory);
        Assert.True(caps.IsCancellable);
        Assert.True(caps.HasTimers);
    }

    [Fact]
    public async Task CancelAsync_DoesNotThrow()
    {
        var agent = Agent(UniqueId("cancel"));
        var ex = await Record.ExceptionAsync(() => agent.CancelAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task EventLog_InitiallyEmpty()
    {
        var agent = Agent(UniqueId("evtlog"));
        var log = await agent.GetEventLogAsync(CancellationToken.None);
        Assert.Empty(log);
    }

    [Fact]
    public async Task HandleEvent_DoesNotThrow()
    {
        var agent = Agent(UniqueId("handle"));
        var evt = new AgentEvent("test", "src", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, new());
        var ex = await Record.ExceptionAsync(() => agent.HandleEventAsync(evt, CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DifferentAgents_HaveSeparateState()
    {
        var a1 = Agent(UniqueId("iso1"));
        var a2 = Agent(UniqueId("iso2"));
        await a1.SetWorkspaceAsync("/ws1", CancellationToken.None);
        await a2.SetWorkspaceAsync("/ws2", CancellationToken.None);
        var s1 = await a1.GetStateAsync(CancellationToken.None);
        var s2 = await a2.GetStateAsync(CancellationToken.None);
        Assert.Equal("/ws1", s1.Entries["workspace-path"].Value);
        Assert.Equal("/ws2", s2.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task GetResponseStream_DoesNotThrow()
    {
        var agent = Agent(UniqueId("stream"));
        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", CancellationToken.None))
            chunks.Add(chunk);
        Assert.NotNull(chunks);
    }
}
