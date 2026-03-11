using Core.AI;
using Core.AI.Models;
using Core.Contracts;
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

        LLMModel.EnsureAllModelsLoaded();
        var mockClient = new MockChatClient().ReturnsText("mock-response");

        siloBuilder.Services.AddSingleton<IChatClient>(mockClient);
        siloBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new MockEmbeddingGenerator());
        siloBuilder.Services.AddHttpClient();
        siloBuilder.Services.AddSingleton<Octokit.IGitHubClient>(
            new Octokit.GitHubClient(new Octokit.ProductHeaderValue("iaw-test")));

        RegisterLlmMapper<Claude45Haiku>(siloBuilder, mockClient);
        RegisterLlmMapper<Sonnet46>(siloBuilder, mockClient);
        RegisterLlmMapper<Opus46>(siloBuilder, mockClient);
        RegisterLlmMapper<Gpt4o>(siloBuilder, mockClient);
        RegisterLlmMapper<Gpt4oMini>(siloBuilder, mockClient);
        RegisterLlmMapper<Gpt52>(siloBuilder, mockClient);
        RegisterLlmMapper<Gpt53>(siloBuilder, mockClient);
        RegisterLlmMapper<Gemini31>(siloBuilder, mockClient);
        RegisterLlmMapper<GrokLatest>(siloBuilder, mockClient);
        RegisterLlmMapper<GitHubGpt4oMini>(siloBuilder, mockClient);
        RegisterLlmMapper<GitHubGpt4o>(siloBuilder, mockClient);
        RegisterLlmMapper<Llama32>(siloBuilder, mockClient);
        RegisterLlmMapper<Qwen25>(siloBuilder, mockClient);
    }

    static void RegisterLlmMapper<TModel>(ISiloBuilder siloBuilder, IChatClient mockClient)
        where TModel : LLMModel
    {
        siloBuilder.Services.AddSingleton<IAttributeToFactoryMapper<LlmAttribute<TModel>>,
            LlmAttributeMapper<TModel>>();

        var model = LLMModel.All.FirstOrDefault(m => m is TModel);
        if (model is not null)
            siloBuilder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey, mockClient);
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
        var specificInterface = typeof(TAgent).GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && typeof(IGrainWithStringKey).IsAssignableFrom(i));

        if (specificInterface is not null)
            return (IAgent)Cluster.GrainFactory.GetGrain(specificInterface, id);

        return Cluster.GrainFactory.GetGrain<IAgent>(id);
    }

    protected string UniqueId(string prefix) => $"{prefix}-{_testRunId}";
}
