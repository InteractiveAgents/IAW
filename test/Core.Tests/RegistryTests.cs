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

    private IAgentRegistry Registry() => _cluster.GrainFactory.GetGrain<IAgentRegistry>("test-registry");

    static AgentRecord MakeRecord(string agentType, string displayName = "", string description = "", string ns = "test", string interfaceName = "") =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentType = agentType,
            DisplayName = displayName.Length > 0 ? displayName : agentType,
            Description = description,
            Namespace = ns,
            InterfaceName = interfaceName.Length > 0 ? interfaceName : $"I{agentType}",
            Capabilities = []
        };

    [Fact]
    public async Task Registry_RegisterAndGetByType()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Registry();
        var record = MakeRecord("TestBot", "Test Bot");
        await registry.RegisterAsync(record, ct);
        var result = await registry.GetByAgentTypeAsync("TestBot", ct);
        Assert.NotNull(result);
        Assert.Equal("TestBot", result.AgentType);
        Assert.Equal("Test Bot", result.DisplayName);
    }

    [Fact]
    public async Task Registry_SearchByKeyword_ReturnsMatches()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Registry();
        await registry.RegisterAsync(MakeRecord("CodeAgent", description: "Generates C# code and runs builds"), ct);
        await registry.RegisterAsync(MakeRecord("WeatherAgent", description: "Provides weather forecasts"), ct);
        var results = await registry.SearchAsync("code build", ct: ct);
        Assert.Contains(results, c => c.AgentType == "CodeAgent");
    }

    [Fact]
    public async Task Registry_GetAll_ReturnsRegistered()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Registry();
        await registry.RegisterAsync(MakeRecord("AgentA", displayName: "A"), ct);
        await registry.RegisterAsync(MakeRecord("AgentB", displayName: "B"), ct);
        var all = await registry.GetAllAsync(ct);
        Assert.True(all.Count >= 2);
        Assert.Contains(all, r => r.AgentType == "AgentA");
        Assert.Contains(all, r => r.AgentType == "AgentB");
    }

    [Fact]
    public async Task Registry_ToPromptString_GroupsByNamespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Registry();
        await registry.RegisterAsync(MakeRecord("ShellAgent", ns: "system", description: "Runs shell commands", interfaceName: "IShell"), ct);
        await registry.RegisterAsync(MakeRecord("RoslynAgent", ns: "coding", description: "Analyzes C# code", interfaceName: "IRoslyn"), ct);
        var prompt = await registry.ToPromptStringAsync(ct);
        Assert.Contains("system", prompt);
        Assert.Contains("coding", prompt);
        Assert.Contains("IShell", prompt);
        Assert.Contains("IRoslyn", prompt);
    }

    [Fact]
    public async Task Registry_SearchWithNamespaceFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Registry();
        await registry.RegisterAsync(MakeRecord("AgentX", ns: "alpha", description: "handles search"), ct);
        await registry.RegisterAsync(MakeRecord("AgentY", ns: "beta", description: "handles search"), ct);
        var results = await registry.SearchAsync("search", namespaceFilter: "alpha", ct: ct);
        Assert.All(results, c => Assert.Equal("alpha", c.Namespace));
    }

    [Fact]
    public async Task Registry_GetByAgentType_ReturnsNull_ForUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Registry();
        var result = await registry.GetByAgentTypeAsync("NonExistentAgent", ct);
        Assert.Null(result);
    }
}
