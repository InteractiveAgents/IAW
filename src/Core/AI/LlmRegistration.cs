using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.ClientModel;
using OllamaSharp;

namespace IAW.Core.AI;

public static class LlmRegistration
{
    public static IHostApplicationBuilder AddLlmProviders(this IHostApplicationBuilder builder)
    {
        LLMModel.EnsureAllModelsLoaded();
        var config = builder.Configuration;

        var declaredModels = ReadDeclaredModels(config);
        var modelsToRegister = declaredModels.Count > 0
            ? declaredModels
            : [.. LLMModel.All.Where(m => IsProviderConfigured(config, m.Provider))];

        foreach (var model in modelsToRegister)
        {
            if (!IsProviderConfigured(config, model.Provider))
                continue;

            builder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey,
                (sp, key) => CreateChatClient(sp, config, model));

            RegisterAttributeMapper(builder.Services, model);
        }

        var firstConfigured = modelsToRegister
            .FirstOrDefault(m => IsProviderConfigured(config, m.Provider));
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

    public static bool IsProviderConfigured(IConfiguration config, ProviderType provider)
    {
        return provider switch
        {
            ProviderType.Ollama => !string.IsNullOrEmpty(config[LlmConfig.OllamaEndpoint])
                                   || !string.IsNullOrEmpty(config["ConnectionStrings:ollama"])
                                   || HasOllamaModelConnectionString(config),
            ProviderType.Anthropic => !string.IsNullOrEmpty(config[LlmConfig.AnthropicApiKey]),
            ProviderType.OpenAI => !string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]),
            ProviderType.GitHub => !string.IsNullOrEmpty(config[LlmConfig.GitHubModelsApiKey]),
            _ => false
        };
    }

    private static bool HasOllamaModelConnectionString(IConfiguration config)
    {
        var connectionStrings = config.GetSection("ConnectionStrings");
        return connectionStrings.GetChildren().Any(c =>
            c.Key.StartsWith("ollama-", StringComparison.OrdinalIgnoreCase));
    }

    internal static IChatClient CreateChatClient(IServiceProvider services, IConfiguration config, LLMModel model)
    {
        var innerClient = model.Provider switch
        {
            ProviderType.Ollama => CreateOllamaClient(config, model),
            ProviderType.Anthropic => CreateAnthropicClient(config, model),
            ProviderType.OpenAI => CreateOpenAiClient(config, model),
            ProviderType.GitHub => CreateGitHubModelsClient(config, model),
            _ => throw new NotSupportedException($"Provider {model.Provider} not supported")
        };

        return new ChatClientBuilder(innerClient)
            .UseOpenTelemetry(
                loggerFactory: services.GetService<ILoggerFactory>(),
                sourceName: "IAW",
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
        // Aspire generates connection names like "ollama-qwen2-5" for model id "qwen2.5"
        var sanitizedId = model.Id.Replace(".", "-").Replace(":", "-");
        return config[$"ConnectionStrings:ollama-{sanitizedId}"];
    }

    private static string? ParseOllamaEndpoint(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return null;

        // Aspire Ollama connection strings use "Endpoint=http://...;Model=..." format
        if (connectionString.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
            return connectionString.Split(';')[0]["Endpoint=".Length..];

        // Plain URI format
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
            builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("embedding",
                (_, _) => new OpenAI.OpenAIClient(
                        new ApiKeyCredential(token),
                        new OpenAI.OpenAIClientOptions { Endpoint = new Uri(LlmConfig.GitHubModelsEndpoint) })
                    .GetEmbeddingClient("text-embedding-3-small")
                    .AsIEmbeddingGenerator());
        }
        else if (!string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]))
        {
            var apiKey = config[LlmConfig.OpenAiApiKey]!;
            builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("embedding",
                (_, _) => new OpenAI.OpenAIClient(apiKey)
                    .GetEmbeddingClient("text-embedding-3-small")
                    .AsIEmbeddingGenerator());
        }
        else
        {
            builder.Services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("embedding",
                (_, _) => throw new InvalidOperationException(
                    "No embedding provider configured. Set AI:LLM:GitHubToken or AI:LLM:OpenAiApiKey."));
        }

        return builder;
    }
}
