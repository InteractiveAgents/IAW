using Aspire.Hosting.Orleans;
using Core.AI;

namespace Aspire;

public static class IAWExtensions
{
    private static IDistributedApplicationBuilder? _appBuilder;
    private static readonly List<LLMModel> _declaredModels = [];
    private static readonly HashSet<ProviderType> _declaredProviders = [];
    private static IResourceBuilder<ParameterResource>? _anthropicKeyParam;
    private static IResourceBuilder<ParameterResource>? _openaiKeyParam;
    private static IResourceBuilder<ParameterResource>? _gitHubTokenParam;
    private static IResourceBuilder<OllamaResource>? _ollamaResource;
    private static readonly List<IResourceBuilder<OllamaModelResource>> _ollamaModelResources = [];

    public static OrleansService AddIAW(
        this IDistributedApplicationBuilder builder,
        string name = "agents")
    {
        _appBuilder = builder;
        _declaredModels.Clear();
        _declaredProviders.Clear();
        _anthropicKeyParam = null;
        _openaiKeyParam = null;
        _gitHubTokenParam = null;
        _ollamaResource = null;
        _ollamaModelResources.Clear();

        return builder.AddOrleans(name)
            .WithClusterId("dev")
            .WithServiceId("dev")
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("Default")
            .WithMemoryGrainStorage("PubSubStore")
            .WithMemoryStreaming("agents")
            .WithMemoryReminders();
    }

    public static OrleansService WithLLM<TModel>(this OrleansService orleans)
        where TModel : LLMModel
    {
        LLMModel.EnsureAllModelsLoaded();
        var model = LLMModel.All.OfType<TModel>().First();

        _declaredModels.Add(model);
        _declaredProviders.Add(model.Provider);

        if (model.Provider == ProviderType.Ollama && _appBuilder is not null)
        {
            _ollamaResource ??= _appBuilder.AddOllama("ollama");
            var modelResource = _ollamaResource.AddModel(model.Id);
            _ollamaModelResources.Add(modelResource);
        }

        return orleans;
    }

    public static OrleansService WithOllama(
        this OrleansService orleans,
        Action<IResourceBuilder<OllamaResource>> configure)
    {
        if (_appBuilder is null)
            throw new InvalidOperationException("Call AddIAW() before WithOllama().");

        _ollamaResource ??= _appBuilder.AddOllama("ollama");
        configure(_ollamaResource);

        return orleans;
    }

    public static IResourceBuilder<T> WithLLMEnvironment<T>(
        this IResourceBuilder<T> builder,
        IDistributedApplicationBuilder appBuilder)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        for (var i = 0; i < _declaredModels.Count; i++)
        {
            var model = _declaredModels[i];
            var prefix = $"AI__LLM__Models__{i}";
            builder.WithEnvironment($"{prefix}__Id", model.Id);
            builder.WithEnvironment($"{prefix}__Provider", model.Provider.ToString());
            builder.WithEnvironment($"{prefix}__ServiceKey", model.ServiceKey);
        }

        if (_declaredProviders.Contains(ProviderType.Anthropic))
        {
            _anthropicKeyParam ??= appBuilder.AddParameter("anthropic-api-key", secret: true);
            builder.WithEnvironment("AI__LLM__AnthropicApiKey", _anthropicKeyParam);
        }

        if (_declaredProviders.Contains(ProviderType.OpenAI))
        {
            _openaiKeyParam ??= appBuilder.AddParameter("openai-api-key", secret: true);
            builder.WithEnvironment("AI__LLM__OpenAiApiKey", _openaiKeyParam);
        }

        _gitHubTokenParam ??= appBuilder.AddParameter("github-token", secret: true);
        builder.WithEnvironment("GitHub__Token", _gitHubTokenParam); // Octokit

        if (_declaredProviders.Contains(ProviderType.GitHub))
            builder.WithEnvironment("AI__LLM__GitHubToken", _gitHubTokenParam);

        foreach (var modelResource in _ollamaModelResources)
        {
            builder.WithReference(modelResource);
            builder.WaitFor(modelResource);
        }

        return builder;
    }
}
