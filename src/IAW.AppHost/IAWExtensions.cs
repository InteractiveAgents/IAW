using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Orleans;
using Core.AI;

namespace Aspire;

public static class IAWExtensions
{
    public static OrleansService AddIAW(
        this IDistributedApplicationBuilder builder,
        string name = "agents")
    {
        return builder.AddOrleans(name)
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("Default")
            .WithMemoryGrainStorage("PubSubStore")
            .WithMemoryStreaming("agents")
            .WithMemoryReminders();
    }

    private static readonly List<LLMModel> _declaredModels = [];
    private static readonly HashSet<ProviderType> _declaredProviders = [];
    private static IResourceBuilder<ParameterResource>? _anthropicKeyParam;
    private static IResourceBuilder<ParameterResource>? _openaiKeyParam;

    public static OrleansService WithLLM<TModel>(this OrleansService orleans)
        where TModel : LLMModel
    {
        LLMModel.EnsureAllModelsLoaded();
        var model = LLMModel.All.OfType<TModel>().First();

        _declaredModels.Add(model);
        _declaredProviders.Add(model.Provider);

        return orleans;
    }

    public static IResourceBuilder<T> WithLLMEnvironment<T>(
        this IResourceBuilder<T> builder,
        IDistributedApplicationBuilder appBuilder)
        where T : IResourceWithEnvironment
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

        return builder;
    }
}
