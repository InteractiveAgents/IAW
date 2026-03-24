using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Core;
using Core.AI;
using Microsoft.Extensions.Configuration;

namespace Aspire.Hosting;

public static class IAWHostingExtensions
{
    public static IAWService AddIAW(
        this IDistributedApplicationBuilder builder,
        string name = "iaw")
    {
        var orleans = builder.AddOrleans(name)
            .WithClusterId("dev")
            .WithServiceId("dev")
            .WithDevelopmentClustering()
            .WithMemoryGrainStorage("Default")
            .WithMemoryGrainStorage("PubSubStore")
            .WithMemoryStreaming(IAWConstants.StreamProvider)
            .WithMemoryReminders();

        var iaw = new IAWService(orleans, builder)
        {
            GitHubTokenParam = builder.AddParameter("github-token", secret: true)
                .WithDescription("Create at [github.com/settings/tokens](https://github.com/settings/tokens) with `repo` scope", enableMarkdown: true)
        };

        var storage = builder.AddAzureStorage("iaw-storage");
        iaw.Blobs = storage.AddBlobs("file-storage");
        iaw.VectorDb = builder.AddQdrant("qdrant");
        iaw.Storage = storage;

        return iaw;
    }

    public static LLMModelBuilder WithLLM<TModel>(this IAWService iaw)
        where TModel : LLMModel
    {
        LLMModel.EnsureAllModelsLoaded();
        var model = LLMModel.All.OfType<TModel>().First();

        if (model.Provider == "tier")
            throw new InvalidOperationException(
                $"Cannot add tier type '{typeof(TModel).Name}' via WithLLM. Use .AsFast(), .AsBalanced(), or .AsReasoning() after WithLLM<ConcreteModel>().");

        iaw.DeclaredModels.Add(model);
        iaw.DeclaredProviders.Add(model.Provider);

        if (model.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            iaw.OllamaResource ??= iaw.AppBuilder.AddOllama("ollama");
            var modelResource = iaw.OllamaResource.AddModel(model.Id);
            iaw.OllamaModelResources.Add(modelResource);
        }

        if (model.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            iaw.AnthropicKeyParam ??= iaw.AppBuilder.AddParameter("anthropic-api-key", secret: true)
                .WithDescription("Get your key at [console.anthropic.com/settings/keys](https://console.anthropic.com/settings/keys)", enableMarkdown: true);

        if (model.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
            iaw.OpenAiKeyParam ??= iaw.AppBuilder.AddParameter("openai-api-key", secret: true)
                .WithDescription("Get your key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys)", enableMarkdown: true);

        return new LLMModelBuilder(iaw, model);
    }

    public static IAWService WithOllama(
        this IAWService iaw,
        Action<IResourceBuilder<OllamaResource>> configure)
    {
        iaw.OllamaResource ??= iaw.AppBuilder.AddOllama("ollama");
        configure(iaw.OllamaResource);
        return iaw;
    }

    public static IAWService WithVoice2Text(this IAWService iaw)
    {
        iaw.WhisperModel = WhisperModel.All.OrderByDescending(m => m.Priority).First();
        return iaw;
    }

    public static IAWService WithVoice2Text<TModel>(this IAWService iaw)
        where TModel : WhisperModel
    {
        iaw.WhisperModel = WhisperModel.All.OfType<TModel>().First();
        return iaw;
    }

    public static IAWService WithStorage(
        this IAWService iaw,
        Action<IResourceBuilder<AzureStorageResource>> configure)
    {
        iaw.StorageCallback = configure;
        return iaw;
    }

    public static IAWService WithVectorDb(
        this IAWService iaw,
        Action<IResourceBuilder<QdrantServerResource>> configure)
    {
        iaw.VectorDbCallback = configure;
        return iaw;
    }

    public static IAWService WithWorkspace(this IAWService iaw, string path)
    {
        iaw.WorkspacePath = path;
        return iaw;
    }

    public static IAWService WithCosmosStorage(
        this IAWService iaw,
        IResourceBuilder<AzureCosmosDBResource> cosmos)
    {
        iaw.Orleans
            .WithGrainStorage("Default", cosmos)
            .WithGrainStorage("PubSubStore", cosmos);
        return iaw;
    }

    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> builder,
        IAWService iaw)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        ApplyInfrastructureDefaults(iaw);

        builder.WithReference(iaw.Orleans);

        builder.WithReference(iaw.Blobs).WaitFor(iaw.Blobs);
        builder.WithReference(iaw.VectorDb).WaitFor(iaw.VectorDb);

        for (var i = 0; i < iaw.DeclaredModels.Count; i++)
        {
            var model = iaw.DeclaredModels[i];
            var prefix = $"AI__LLM__Models__{i}";
            builder.WithEnvironment($"{prefix}__Id", model.Id);
            builder.WithEnvironment($"{prefix}__Provider", model.Provider);
            builder.WithEnvironment($"{prefix}__ServiceKey", model.ServiceKey);
        }

        foreach (var (tierKey, concreteKey) in iaw.TierMappings)
        {
            var tierName = tierKey.Replace("tier-tier-", "");
            builder.WithEnvironment($"AI__LLM__Tiers__{tierName}", concreteKey);
        }

        if (iaw.AnthropicKeyParam is not null)
            builder.WithEnvironment("AI__LLM__AnthropicApiKey", iaw.AnthropicKeyParam);

        if (iaw.OpenAiKeyParam is not null)
            builder.WithEnvironment("AI__LLM__OpenAiApiKey", iaw.OpenAiKeyParam);

        builder.WithEnvironment("GitHub__Token", iaw.GitHubTokenParam);

        if (iaw.DeclaredProviders.Contains("github"))
            builder.WithEnvironment("AI__LLM__GitHubToken", iaw.GitHubTokenParam);

        var waitForLlmModelResources = iaw.AppBuilder.Configuration.GetValue("IAW:WaitForLlmModelResources", false);
        foreach (var modelResource in iaw.OllamaModelResources)
        {
            builder.WithReference(modelResource);
            if (waitForLlmModelResources)
                builder.WaitFor(modelResource);
        }

        if (iaw.WhisperModel is not null)
            builder.WithEnvironment("AI__Whisper__ModelId", iaw.WhisperModel.Id);

        if (!string.IsNullOrEmpty(iaw.WorkspacePath))
            builder.WithEnvironment("IAW__Workspace", iaw.WorkspacePath);

        return builder;
    }

    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> builder,
        IAWClientService client)
        where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    {
        var iaw = client.IAW;
        ApplyInfrastructureDefaults(iaw);

        builder.WithReference(client.OrleansClient);
        builder.WithReference(iaw.Blobs).WaitFor(iaw.Blobs);
        builder.WithReference(iaw.VectorDb).WaitFor(iaw.VectorDb);

        if (!string.IsNullOrEmpty(iaw.WorkspacePath))
            builder.WithEnvironment("IAW__Workspace", iaw.WorkspacePath);

        return builder;
    }

    internal static void ApplyInfrastructureDefaults(IAWService iaw)
    {
        if (iaw.InfrastructureApplied)
            return;
        iaw.InfrastructureApplied = true;

        if (iaw.StorageCallback is not null)
            iaw.StorageCallback(iaw.Storage);
        else
            iaw.Storage.RunAsEmulator(e => e.WithDataVolume("iaw-blobs"));

        if (iaw.VectorDbCallback is not null)
            iaw.VectorDbCallback(iaw.VectorDb);
        else
            iaw.VectorDb.WithDataVolume();
    }
}