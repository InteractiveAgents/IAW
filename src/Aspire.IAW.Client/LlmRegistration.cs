using Core.AI;
using Core.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.ClientModel;

namespace Aspire.IAW;

internal static class LlmRegistration
{
    internal static IHostApplicationBuilder AddLlmProviders(this IHostApplicationBuilder builder)
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

        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(builder.Services);

        foreach (var model in modelsToRegister)
        {
            if (model.Provider == "tier")
                continue;

            if (!IsProviderConfigured(factoryMap, config, model.Provider))
                continue;

            builder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey,
                (sp, key) => CreateChatClient(sp, factoryMap, config, model));
        }

        var firstConfigured = modelsToRegister
            .Where(m => m.Provider != "tier")
            .FirstOrDefault(m => IsProviderConfigured(factoryMap, config, m.Provider));
        if (firstConfigured is not null)
        {
            builder.Services.AddChatClient(services =>
                services.GetRequiredKeyedService<IChatClient>(firstConfigured.ServiceKey));
        }

        var tierNames = new[] { "fast", "balanced", "reasoning" };
        foreach (var tierName in tierNames)
        {
            var concreteKey = config[$"AI:LLM:Tiers:{tierName}"];
            var tierModel = LLMModel.All.FirstOrDefault(m =>
                m.Provider == "tier" && m.Id == $"tier-{tierName}");

            if (tierModel is null) continue;

            if (!string.IsNullOrEmpty(concreteKey))
            {
                builder.Services.AddKeyedSingleton<IChatClient>(tierModel.ServiceKey,
                    (sp, _) => sp.GetRequiredKeyedService<IChatClient>(concreteKey));
            }
            else if (firstConfigured is not null)
            {
                builder.Services.AddKeyedSingleton<IChatClient>(tierModel.ServiceKey,
                    (sp, _) => sp.GetRequiredKeyedService<IChatClient>(firstConfigured.ServiceKey));
            }
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

    private static bool IsProviderConfigured(Dictionary<string, ILlmProviderFactory> factories, IConfiguration config, string provider)
        => factories.TryGetValue(provider, out var factory) && factory.IsConfigured(config);

    private static IChatClient CreateChatClient(IServiceProvider services, Dictionary<string, ILlmProviderFactory> factories, IConfiguration config, LLMModel model)
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

    internal static IHostApplicationBuilder AddEmbeddingProvider(this IHostApplicationBuilder builder)
    {
        var config = builder.Configuration;

        var declaredProvider = config[LlmConfig.EmbeddingProvider];
        var declaredModelId = config[LlmConfig.EmbeddingModelId];

        if (string.Equals(declaredProvider, "ollama", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(declaredModelId))
        {
            var sanitizedId = declaredModelId.Replace(".", "-").Replace(":", "-");
            var modelConnectionString = config[$"ConnectionStrings:ollama-{sanitizedId}"];
            var endpoint = ParseOllamaEndpoint(modelConnectionString)
                ?? config[LlmConfig.OllamaEndpoint]
                ?? config["ConnectionStrings:ollama"]
                ?? "http://localhost:11434";

            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                new OllamaApiClient(new Uri(endpoint), declaredModelId));
        }
        else if (!string.IsNullOrEmpty(config[LlmConfig.GitHubModelsApiKey]))
        {
            var token = config[LlmConfig.GitHubModelsApiKey]!;
            var modelId = declaredModelId ?? "text-embedding-3-small";
            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                _ => new OpenAI.OpenAIClient(
                        new ApiKeyCredential(token),
                        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(LlmConfig.GitHubModelsEndpoint) })
                    .GetEmbeddingClient(modelId)
                    .AsIEmbeddingGenerator());
        }
        else if (!string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]))
        {
            var apiKey = config[LlmConfig.OpenAiApiKey]!;
            var modelId = declaredModelId ?? "text-embedding-3-small";
            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                _ => new OpenAI.OpenAIClient(apiKey)
                    .GetEmbeddingClient(modelId)
                    .AsIEmbeddingGenerator());
        }
        else
        {
            var dimensions = int.TryParse(config[LlmConfig.EmbeddingDimensions], out var d) ? d : 384;
            builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
                new NoOpEmbeddingGenerator(dimensions));
        }

        return builder;
    }

    public static IHostApplicationBuilder AddWhisperProvider<TService>(this IHostApplicationBuilder builder)
        where TService : class, IAudioTranscriptionService
    {
        builder.Services.AddSingleton<IAudioTranscriptionService, TService>();
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