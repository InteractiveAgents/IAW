using Core;
using Core.Contracts;
using Core.Registry;
using IAW.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests;

public sealed class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams(IAWConstants.StreamProvider)
            .UseInMemoryReminderService();

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider,
            VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        siloBuilder.Services.AddSingleton<IChatClient>(new MockChatClient().ReturnsText("mock-response"));
    }
}

public sealed class TestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(Microsoft.Extensions.Configuration.IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams(IAWConstants.StreamProvider);
    }
}

public class RegistryTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IAgentRegistryGrain Registry() => _cluster.GrainFactory.GetGrain<IAgentRegistryGrain>("test-registry");

    [Fact]
    public async Task Registry_RegisterAndGetByType()
    {
        var registry = Registry();
        var reg = new AgentRegistration("TestBot", "Test Bot", "A test agent", [], []);
        await registry.RegisterAsync(reg);
        var result = await registry.GetByTypeAsync("TestBot");
        Assert.NotNull(result);
        Assert.Equal("TestBot", result.AgentType);
        Assert.Equal("Test Bot", result.DisplayName);
    }

    [Fact]
    public async Task Registry_QueryByPublishes_OnlyReturnsPublishers()
    {
        var registry = Registry();
        await registry.RegisterAsync(new AgentRegistration("PublisherOne", "Publisher", "", ["MyEvent"], []));
        await registry.RegisterAsync(new AgentRegistration("NonPublisher", "None", "", [], []));
        var publishers = await registry.QueryAsync(new AgentQuery(Publishes: ["MyEvent"]));
        Assert.Contains(publishers, r => r.AgentType == "PublisherOne");
        Assert.DoesNotContain(publishers, r => r.AgentType == "NonPublisher");
    }

    [Fact]
    public async Task Registry_GetAll_ReturnsRegistered()
    {
        var registry = Registry();
        await registry.RegisterAsync(new AgentRegistration("AgentA", "A", "", [], []));
        await registry.RegisterAsync(new AgentRegistration("AgentB", "B", "", [], []));
        var all = await registry.GetAllAsync();
        Assert.True(all.Count >= 2);
        Assert.Contains(all, r => r.AgentType == "AgentA");
        Assert.Contains(all, r => r.AgentType == "AgentB");
    }

    [Fact]
    public async Task Registry_Unregister_RemovesAgent()
    {
        var registry = Registry();
        await registry.RegisterAsync(new AgentRegistration("ToRemove", "Remove Me", "", [], []));
        await registry.UnregisterAsync("ToRemove");
        var result = await registry.GetByTypeAsync("ToRemove");
        Assert.Null(result);
    }

    [Fact]
    public async Task Registry_QueryByPublishes()
    {
        var registry = Registry();
        await registry.RegisterAsync(new AgentRegistration("PubAgent", "Pub", "", ["BuildCompletedEvent"], []));
        var results = await registry.QueryAsync(new AgentQuery(Publishes: ["BuildCompletedEvent"]));
        Assert.Contains(results, r => r.AgentType == "PubAgent");
    }
}
