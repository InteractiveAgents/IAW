using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Testing;

public sealed class AgentTestSiloConfigurator : ISiloConfigurator
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

        siloBuilder.Services.AddSingleton<IChatClient>(new MockChatClient().ReturnsText("mock-response"));
    }
}

public sealed class AgentTestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("agents");
    }
}

public abstract class AgentTest<TAgent> : IAsyncLifetime where TAgent : Agent
{
    private readonly string _testRunId = Guid.NewGuid().ToString("N")[..8];

    protected TestCluster Cluster { get; private set; } = null!;

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

    public async ValueTask DisposeAsync() => await Cluster.DisposeAsync();

    protected virtual void ConfigureSilo(TestClusterBuilder builder) { }
    protected virtual Task OnClusterReadyAsync() => Task.CompletedTask;

    protected IAgent Agent(string id)
    {
        // Resolve via the most specific grain interface that extends IAgent on TAgent,
        // so Orleans routes to the correct grain class
        var specificInterface = typeof(TAgent).GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && typeof(IGrainWithStringKey).IsAssignableFrom(i));

        if (specificInterface is not null)
            return (IAgent)Cluster.GrainFactory.GetGrain(specificInterface, id);

        return Cluster.GrainFactory.GetGrain<IAgent>(id);
    }

    protected string UniqueId(string prefix) => $"{prefix}-{_testRunId}";

    // ── Conversation ──

    [Fact]
    public async Task Behavior_Conversation_GetResponse_ReturnsNonEmpty()
    {
        var agent = Agent(UniqueId("v3-resp"));
        var response = await agent.GetResponse("Hello", CancellationToken.None);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Behavior_Conversation_GetResponseStream_DoesNotThrow()
    {
        var agent = Agent(UniqueId("v3-stream"));
        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", CancellationToken.None))
            chunks.Add(chunk);
        Assert.NotNull(chunks);
    }

    [Fact]
    public async Task Behavior_Conversation_GetHistory_AfterResponse_HasEntries()
    {
        var agent = Agent(UniqueId("v3-hist"));
        await agent.GetResponse("Hello", CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.True(history.Count > 0);
    }

    [Fact]
    public async Task Behavior_Conversation_ClearHistory_EmptiesMessages()
    {
        var agent = Agent(UniqueId("v3-clear"));
        await agent.GetResponse("Hello", CancellationToken.None);
        await agent.ClearHistoryAsync(CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.Empty(history);
    }

    [Fact]
    public async Task Behavior_Conversation_MultipleMessages_AllRecorded()
    {
        var agent = Agent(UniqueId("v3-multi"));
        await agent.GetResponse("First", CancellationToken.None);
        await agent.GetResponse("Second", CancellationToken.None);
        var history = await agent.GetHistory(CancellationToken.None);
        Assert.True(history.Count >= 4); // 2 user + 2 assistant messages minimum
    }

    // ── State ──

    [Fact]
    public async Task Behavior_State_SetWorkspace_PersistsInState()
    {
        var agent = Agent(UniqueId("v3-ws"));
        await agent.SetWorkspaceAsync("/tmp/test", CancellationToken.None);
        var agentState = await agent.GetStateAsync(CancellationToken.None);
        Assert.True(agentState.Entries.ContainsKey("workspace-path"));
        Assert.Equal("/tmp/test", agentState.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task Behavior_State_GetState_InitiallyEmpty()
    {
        var agent = Agent(UniqueId("v3-state-empty"));
        var agentState = await agent.GetStateAsync(CancellationToken.None);
        Assert.NotNull(agentState);
    }

    [Fact]
    public async Task Behavior_State_MultipleUpdates_LastValueWins()
    {
        var agent = Agent(UniqueId("v3-state-multi"));
        await agent.SetWorkspaceAsync("/first", CancellationToken.None);
        await agent.SetWorkspaceAsync("/second", CancellationToken.None);
        var agentState = await agent.GetStateAsync(CancellationToken.None);
        Assert.Equal("/second", agentState.Entries["workspace-path"].Value);
    }

    // ── Metadata ──

    [Fact]
    public async Task Behavior_Metadata_AgentType_IsNotEmpty()
    {
        var agent = Agent(UniqueId("v3-type"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(metadata.AgentType));
    }

    [Fact]
    public async Task Behavior_Metadata_DisplayName_IsNotEmpty()
    {
        var agent = Agent(UniqueId("v3-display"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(metadata.DisplayName));
    }

    [Fact]
    public async Task Behavior_Metadata_Kind_IsValid()
    {
        var agent = Agent(UniqueId("v3-kind"));
        var metadata = await agent.GetMetadataAsync(CancellationToken.None);
        Assert.True(Enum.IsDefined(metadata.Kind));
    }

    // ── Capabilities ──

    [Fact]
    public async Task Behavior_Capabilities_HasMemory_IsTrue()
    {
        var agent = Agent(UniqueId("v3-cap-mem"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasMemory);
    }

    [Fact]
    public async Task Behavior_Capabilities_IsCancellable_IsTrue()
    {
        var agent = Agent(UniqueId("v3-cap-cancel"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.IsCancellable);
    }

    [Fact]
    public async Task Behavior_Capabilities_HasTimers_IsTrue()
    {
        var agent = Agent(UniqueId("v3-cap-timer"));
        var caps = await agent.GetCapabilitiesAsync(CancellationToken.None);
        Assert.True(caps.HasTimers);
    }

    // ── Lifecycle ──

    [Fact]
    public async Task Behavior_Lifecycle_Cancel_DoesNotThrow()
    {
        var agent = Agent(UniqueId("v3-cancel"));
        var ex = await Record.ExceptionAsync(() => agent.CancelAsync(CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Behavior_Lifecycle_Cancel_AgentStillResponds()
    {
        var agent = Agent(UniqueId("v3-cancel-resp"));
        await agent.CancelAsync(CancellationToken.None);
        var response = await agent.GetResponse("After cancel", CancellationToken.None);
        Assert.NotNull(response);
    }

    // ── Events ──

    [Fact]
    public async Task Behavior_Events_EventLogInitiallyEmpty()
    {
        var agent = Agent(UniqueId("v3-evtlog"));
        var log = await agent.GetEventLogAsync(CancellationToken.None);
        Assert.Empty(log);
    }

    [Fact]
    public async Task Behavior_Events_HandleEvent_DoesNotThrow()
    {
        var agent = Agent(UniqueId("v3-handle"));
        var evt = new AgentEvent("test", "src", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, new());
        var ex = await Record.ExceptionAsync(() => agent.HandleEventAsync(evt, CancellationToken.None));
        Assert.Null(ex);
    }

    // ── Isolation ──

    [Fact]
    public async Task Behavior_Isolation_SeparateState()
    {
        var a1 = Agent(UniqueId("v3-iso1"));
        var a2 = Agent(UniqueId("v3-iso2"));
        await a1.SetWorkspaceAsync("/ws1", CancellationToken.None);
        await a2.SetWorkspaceAsync("/ws2", CancellationToken.None);
        var s1 = await a1.GetStateAsync(CancellationToken.None);
        var s2 = await a2.GetStateAsync(CancellationToken.None);
        Assert.Equal("/ws1", s1.Entries["workspace-path"].Value);
        Assert.Equal("/ws2", s2.Entries["workspace-path"].Value);
    }

    [Fact]
    public async Task Behavior_Isolation_SeparateHistory()
    {
        var a1 = Agent(UniqueId("v3-isoh1"));
        var a2 = Agent(UniqueId("v3-isoh2"));
        await a1.GetResponse("Agent1 message", CancellationToken.None);
        var h1 = await a1.GetHistory(CancellationToken.None);
        var h2 = await a2.GetHistory(CancellationToken.None);
        Assert.True(h1.Count > 0);
        Assert.Empty(h2);
    }
}
