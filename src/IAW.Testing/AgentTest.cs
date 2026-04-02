using Core;
using Core.AI;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.BroadcastChannel;
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
            .AddMemoryStreams(IAWConstants.StreamProvider)
            .UseInMemoryReminderService()
            .UseInMemoryDurableJobs();

        siloBuilder.AddBroadcastChannel(IAWConstants.UIBroadcastProvider);

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        var mockClient = new MockChatClient().ReturnsText("mock-response");

        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(siloBuilder.Services, mockClient);

        siloBuilder.Services.AddSingleton<IChatClient>(mockClient);
        siloBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new MockEmbeddingGenerator());
        siloBuilder.Services.AddHttpClient();
        siloBuilder.Services.AddSingleton<Octokit.IGitHubClient>(
            new Octokit.GitHubClient(new Octokit.ProductHeaderValue("iaw-test")));
    }
}

public sealed class AgentTestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams(IAWConstants.StreamProvider);
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
        // Find the most specific IAgent-derived interface (leaf, not a base like IMemoryAgent)
        var agentInterfaces = typeof(TAgent).GetInterfaces()
            .Where(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && typeof(IGrainWithStringKey).IsAssignableFrom(i))
            .ToList();

        // Prefer leaf interfaces: exclude any interface that is a base of another candidate
        var specificInterface = agentInterfaces
            .FirstOrDefault(i => !agentInterfaces.Any(other => other != i && i.IsAssignableFrom(other)))
            ?? agentInterfaces.FirstOrDefault();

        if (specificInterface is not null)
            return (IAgent)Cluster.GrainFactory.GetGrain(specificInterface, id);

        return Cluster.GrainFactory.GetGrain<IAgent>(id);
    }

    protected string UniqueId(string prefix) => $"{prefix}-{_testRunId}";
}