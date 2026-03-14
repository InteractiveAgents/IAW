using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ClientModel;
using OllamaSharp;
using Core.Contracts;
using Core.Observability;

namespace Core.AI;

public static class LlmRegistration
{
    public static IHostApplicationBuilder AddLlmProviders(this IHostApplicationBuilder builder)
    {
        LLMModel.EnsureAllModelsLoaded();
        var config = builder.Configuration;

        var factories = new ILlmProviderFactory[]
        {
            new AnthropicProviderFactory(),
            new OpenAIProviderFactory(),
            new OllamaProviderFactory(),
            new GitHubProviderFactory()
        };
        foreach (var f in factories)
            builder.Services.AddSingleton<ILlmProviderFactory>(f);

        var factoryMap = factories.ToDictionary(f => f.ProviderName, StringComparer.OrdinalIgnoreCase);

        var declaredModels = ReadDeclaredModels(config);
        var modelsToRegister = declaredModels.Count > 0
            ? declaredModels
            : [.. LLMModel.All.Where(m => IsProviderConfigured(factoryMap, config, m.Provider))];

        foreach (var model in LLMModel.All)
            RegisterAttributeMapper(builder.Services, model);

        builder.Services.AddSingleton<IAttributeToFactoryMapper<AgentStateAttribute>, AgentStateMapper>();
        builder.Services.AddSingleton<IAttributeToFactoryMapper<UserProfileStateAttribute>, UserProfileStateMapper>();
        builder.Services.AddSingleton<IAttributeToFactoryMapper<ProjectStateAttribute>, ProjectStateMapper>();
        builder.Services.AddSingleton<IAttributeToFactoryMapper<UISessionStateAttribute>, UISessionStateMapper>();

        foreach (var model in modelsToRegister)
        {
            if (!IsProviderConfigured(factoryMap, config, model.Provider))
                continue;

            builder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey,
                (sp, key) => CreateChatClient(sp, factoryMap, config, model));
        }

        var firstConfigured = modelsToRegister
            .FirstOrDefault(m => IsProviderConfigured(factoryMap, config, m.Provider));
        if (firstConfigured is not null)
        {
            builder.Services.AddChatClient(services =>
                services.GetRequiredKeyedService<IChatClient>(firstConfigured.ServiceKey));
        }

        return builder;
    }

    private static List<LLMModel> ReadDeclaredModels(IConfiguration config)
    {
        var result = new List<LLMModel>();
        var modelsSection = config.GetSection("AI:LLM:Models");
        if (!modelsSection.Exists())
            return result;

        foreach (var child in modelsSection.GetChildren())
        {
            var id = child["Id"];
            var serviceKey = child["ServiceKey"];
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(serviceKey))
                continue;

            var matchedModel = LLMModel.All.FirstOrDefault(m =>
                string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase));

            if (matchedModel is not null)
                result.Add(matchedModel);
        }

        return result;
    }

    private static void RegisterAttributeMapper(IServiceCollection services, LLMModel model)
    {
        var modelType = model.GetType();
        var mapperType = typeof(LlmAttributeMapper<>).MakeGenericType(modelType);
        var attributeType = typeof(LlmAttribute<>).MakeGenericType(modelType);
        var interfaceType = typeof(IAttributeToFactoryMapper<>).MakeGenericType(attributeType);
        services.AddSingleton(interfaceType, mapperType);
    }

    public static bool IsProviderConfigured(Dictionary<string, ILlmProviderFactory> factories, IConfiguration config, string provider)
        => factories.TryGetValue(provider, out var factory) && factory.IsConfigured(config);

    internal static IChatClient CreateChatClient(IServiceProvider services, Dictionary<string, ILlmProviderFactory> factories, IConfiguration config, LLMModel model)
    {
        if (!factories.TryGetValue(model.Provider, out var factory))
            throw new NotSupportedException($"Provider '{model.Provider}' not supported. Register an ILlmProviderFactory.");

        var innerClient = factory.CreateClient(model, config);

        return new ChatClientBuilder(innerClient)
            .UseStreamingUsage()
            .UseOpenTelemetry(
                loggerFactory: services.GetService<ILoggerFactory>(),
                configure: telemetry => telemetry.EnableSensitiveData = true)
            .Build(services);
    }

    private static IChatClient CreateOllamaClient(IConfiguration config, LLMModel model)
    {
        var modelConnectionString = FindOllamaModelConnectionString(config, model);
        var endpoint = ParseOllamaEndpoint(modelConnectionString)
            ?? config[LlmConfig.OllamaEndpoint]
            ?? config["ConnectionStrings:ollama"]
            ?? "http://localhost:11434";
        return new OllamaApiClient(new Uri(endpoint), model.Id);
    }

    private static string? FindOllamaModelConnectionString(IConfiguration config, LLMModel model)
    {
        var sanitizedId = model.Id.Replace(".", "-").Replace(":", "-");
        return config[$"ConnectionStrings:ollama-{sanitizedId}"];
    }

    private static string? ParseOllamaEndpoint(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return null;

        if (connectionString.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
            return connectionString.Split(';')[0]["Endpoint=".Length..];

        if (connectionString.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        return null;
    }

    private static IChatClient CreateAnthropicClient(IConfiguration config, LLMModel model)
    {
        var apiKey = config[LlmConfig.AnthropicApiKey]
            ?? throw new InvalidOperationException("Anthropic API key not configured.");
        var client = new Anthropic.AnthropicClient { ApiKey = apiKey };
        return client.AsIChatClient(model.Id);
    }

    private static IChatClient CreateOpenAiClient(IConfiguration config, LLMModel model)
    {
        var apiKey = config[LlmConfig.OpenAiApiKey]
            ?? throw new InvalidOperationException("OpenAI API key not configured.");
        return new OpenAI.OpenAIClient(apiKey)
            .GetChatClient(model.Id)
            .AsIChatClient();
    }

    private static IChatClient CreateGitHubModelsClient(IConfiguration config, LLMModel model)
    {
        var token = config[LlmConfig.GitHubModelsApiKey]
            ?? throw new InvalidOperationException("GitHub token not configured for GitHub Models.");
        return new OpenAI.OpenAIClient(
                new ApiKeyCredential(token),
                new OpenAI.OpenAIClientOptions { Endpoint = new Uri(LlmConfig.GitHubModelsEndpoint) })
            .GetChatClient(model.Id)
            .AsIChatClient();
    }

    public static IHostApplicationBuilder AddEmbeddingProvider(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration;

        if (!string.IsNullOrEmpty(config[LlmConfig.GitHubModelsApiKey]))
        {
            var token = config[LlmConfig.GitHubModelsApiKey]!;
            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                _ => new OpenAI.OpenAIClient(
                        new ApiKeyCredential(token),
                        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(LlmConfig.GitHubModelsEndpoint) })
                    .GetEmbeddingClient("text-embedding-3-small")
                    .AsIEmbeddingGenerator());
        }
        else if (!string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]))
        {
            var apiKey = config[LlmConfig.OpenAiApiKey]!;
            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                _ => new OpenAI.OpenAIClient(apiKey)
                    .GetEmbeddingClient("text-embedding-3-small")
                    .AsIEmbeddingGenerator());
        }
        else
        {
            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                _ => throw new InvalidOperationException(
                    "No embedding provider configured. Set AI:LLM:GitHubToken or AI:LLM:OpenAiApiKey."));
        }

        return builder;
    }

    public static IHostApplicationBuilder AddWhisperProvider(this IHostApplicationBuilder builder)
    {
        WhisperModel.EnsureAllModelsLoaded();
        builder.Services.AddSingleton<IAudioTranscriptionService, FoundryLocalTranscriptionService>();
        return builder;
    }

    private sealed class AnthropicProviderFactory : ILlmProviderFactory
    {
        public string ProviderName => "anthropic";
        public bool IsConfigured(IConfiguration config)
            => !string.IsNullOrEmpty(config[LlmConfig.AnthropicApiKey]);
        public IChatClient CreateClient(LLMModel model, IConfiguration config)
            => CreateAnthropicClient(config, model);
    }

    private sealed class OpenAIProviderFactory : ILlmProviderFactory
    {
        public string ProviderName => "openai";
        public bool IsConfigured(IConfiguration config)
            => !string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]);
        public IChatClient CreateClient(LLMModel model, IConfiguration config)
            => CreateOpenAiClient(config, model);
    }

    private sealed class OllamaProviderFactory : ILlmProviderFactory
    {
        public string ProviderName => "ollama";
        public bool IsConfigured(IConfiguration config)
            => !string.IsNullOrEmpty(config[LlmConfig.OllamaEndpoint])
               || !string.IsNullOrEmpty(config["ConnectionStrings:ollama"])
               || HasOllamaModelConnectionString(config);
        public IChatClient CreateClient(LLMModel model, IConfiguration config)
            => CreateOllamaClient(config, model);
    }

    private sealed class GitHubProviderFactory : ILlmProviderFactory
    {
        public string ProviderName => "github";
        public bool IsConfigured(IConfiguration config)
            => !string.IsNullOrEmpty(config[LlmConfig.GitHubModelsApiKey]);
        public IChatClient CreateClient(LLMModel model, IConfiguration config)
            => CreateGitHubModelsClient(config, model);
    }

    private static bool HasOllamaModelConnectionString(IConfiguration config)
    {
        var connectionStrings = config.GetSection("ConnectionStrings");
        return connectionStrings.GetChildren().Any(c =>
            c.Key.StartsWith("ollama-", StringComparison.OrdinalIgnoreCase));
    }
}
