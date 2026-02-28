# LLM Model System - Full Port Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Port the complete LLM model infrastructure from the outer IAW repo into the inner Core project, enabling `WithLLM<TModel>()` in AppHost and keyed `IChatClient` DI registration.

**Architecture:** Abstract `LLMModel` base class with static registry, concrete model singletons (Sonnet46, Claude45Haiku, Gpt4o, Gpt4oMini, Llama32), Orleans facet-based `[Llm<TModel>]` attribute for constructor injection, and `AddLlmProviders()` host builder extension that registers keyed `IChatClient` per declared model. AppHost uses `WithLLM<TModel>()` to declare models and `WithLLMEnvironment()` to inject config into resources.

**Tech Stack:** .NET 11, Orleans 10, Microsoft.Extensions.AI, Anthropic SDK, OpenAI SDK, OllamaSharp

---

### Task 1: Add NuGet package versions to Directory.Packages.props

**Files:**
- Modify: `Directory.Packages.props`

**Step 1: Add AI package versions**

Add these entries to the `<ItemGroup>` in `Directory.Packages.props`:

```xml
<PackageVersion Include="Anthropic" Version="12.5.0" />
<PackageVersion Include="Microsoft.Extensions.AI" Version="10.3.0" />
<PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.3.0" />
<PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="10.3.0" />
<PackageVersion Include="OpenAI" Version="2.8.0" />
<PackageVersion Include="OllamaSharp" Version="5.4.16" />
```

**Step 2: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: Success (no packages consumed yet)

**Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "feat: add AI provider NuGet package versions"
```

---

### Task 2: Add package references to Core.csproj

**Files:**
- Modify: `src/Core/Core.csproj`

**Step 1: Add AI package references**

Add to the existing `<ItemGroup>` in Core.csproj:

```xml
<PackageReference Include="Anthropic" />
<PackageReference Include="Microsoft.Extensions.AI" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" />
<PackageReference Include="OpenAI" />
<PackageReference Include="OllamaSharp" />
```

**Step 2: Build Core to verify restore**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success

**Step 3: Commit**

```bash
git add src/Core/Core.csproj
git commit -m "feat: add AI provider package references to Core"
```

---

### Task 3: Create ProviderType and ModelCapabilities

**Files:**
- Create: `src/Core/AI/ProviderType.cs`
- Create: `src/Core/AI/ModelCapabilities.cs`

**Step 1: Create ProviderType.cs**

```csharp
namespace Core.AI;

public enum ProviderType
{
    Ollama,
    Anthropic,
    OpenAI
}
```

**Step 2: Create ModelCapabilities.cs**

```csharp
namespace Core.AI;

public sealed record ModelCapabilities(
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsStreaming,
    bool SupportsStructuredOutput)
{
    public static ModelCapabilities FullyCapable => new(true, true, true, true);
    public static ModelCapabilities ChatOnly => new(false, false, true, false);
    public static ModelCapabilities ToolCapable => new(true, false, true, true);
}
```

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success

**Step 4: Commit**

```bash
git add src/Core/AI/
git commit -m "feat: add ProviderType enum and ModelCapabilities record"
```

---

### Task 4: Create LLMModel base class

**Files:**
- Create: `src/Core/AI/LLMModel.cs`

**Step 1: Create LLMModel.cs**

```csharp
namespace Core.AI;

public abstract class LLMModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract ProviderType Provider { get; }
    public abstract ModelCapabilities Capabilities { get; }

    public bool IsLocal => Provider == ProviderType.Ollama;

    public string ServiceKey
    {
        get
        {
            var normalizedId = Id.ToLowerInvariant()
                .Replace(".", "")
                .Replace(":", "-");
            return $"{Provider.ToString().ToLowerInvariant()}-{normalizedId}";
        }
    }

    private static readonly List<LLMModel> _registry = [];
    private static readonly Lock _lock = new();

    public static IReadOnlyList<LLMModel> All
    {
        get { lock (_lock) { return _registry.ToList(); } }
    }

    protected LLMModel()
    {
        lock (_lock) { _registry.Add(this); }
    }

    public static void EnsureAllModelsLoaded()
    {
        _ = Models.Claude45Haiku.Instance;
        _ = Models.Sonnet46.Instance;
        _ = Models.Gpt4o.Instance;
        _ = Models.Gpt4oMini.Instance;
        _ = Models.Llama32.Instance;
    }
}
```

Note: This will not compile until Task 5 (models) is complete. That's expected.

**Step 2: Commit (compile deferred to after Task 5)**

```bash
git add src/Core/AI/LLMModel.cs
git commit -m "feat: add LLMModel abstract base class with static registry"
```

---

### Task 5: Create concrete model classes

**Files:**
- Create: `src/Core/AI/Models/Sonnet46.cs`
- Create: `src/Core/AI/Models/Claude45Haiku.cs`
- Create: `src/Core/AI/Models/Gpt4o.cs`
- Create: `src/Core/AI/Models/Gpt4oMini.cs`
- Create: `src/Core/AI/Models/Llama32.cs`

**Step 1: Create Sonnet46.cs**

```csharp
namespace Core.AI.Models;

public sealed class Sonnet46 : LLMModel
{
    public static readonly Sonnet46 Instance = new();
    private Sonnet46() { }

    public override string Id => "claude-sonnet-4-6";
    public override string DisplayName => "Claude Sonnet 4.6";
    public override ProviderType Provider => ProviderType.Anthropic;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
```

**Step 2: Create Claude45Haiku.cs**

```csharp
namespace Core.AI.Models;

public sealed class Claude45Haiku : LLMModel
{
    public static readonly Claude45Haiku Instance = new();
    private Claude45Haiku() { }

    public override string Id => "claude-haiku-4-5-20251001";
    public override string DisplayName => "Claude 4.5 Haiku";
    public override ProviderType Provider => ProviderType.Anthropic;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
```

**Step 3: Create Gpt4o.cs**

```csharp
namespace Core.AI.Models;

public sealed class Gpt4o : LLMModel
{
    public static readonly Gpt4o Instance = new();
    private Gpt4o() { }

    public override string Id => "gpt-4o";
    public override string DisplayName => "GPT-4o";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
```

**Step 4: Create Gpt4oMini.cs**

```csharp
namespace Core.AI.Models;

public sealed class Gpt4oMini : LLMModel
{
    public static readonly Gpt4oMini Instance = new();
    private Gpt4oMini() { }

    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GPT-4o Mini";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
```

**Step 5: Create Llama32.cs**

```csharp
namespace Core.AI.Models;

public sealed class Llama32 : LLMModel
{
    public static readonly Llama32 Instance = new();
    private Llama32() { }

    public override string Id => "llama3.2";
    public override string DisplayName => "Llama 3.2";
    public override ProviderType Provider => ProviderType.Ollama;
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}
```

**Step 6: Build to verify compilation**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success (LLMModel + all 5 models compile together)

**Step 7: Commit**

```bash
git add src/Core/AI/Models/ src/Core/AI/LLMModel.cs
git commit -m "feat: add 5 concrete LLM model classes"
```

---

### Task 6: Create LlmConfig constants

**Files:**
- Create: `src/Core/AI/LlmConfig.cs`

**Step 1: Create LlmConfig.cs**

```csharp
namespace Core.AI;

public static class LlmConfig
{
    public const string AnthropicApiKey = "AI:LLM:AnthropicApiKey";
    public const string OpenAiApiKey = "AI:LLM:OpenAiApiKey";
    public const string OllamaEndpoint = "AI:LLM:OllamaEndpoint";
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success

**Step 3: Commit**

```bash
git add src/Core/AI/LlmConfig.cs
git commit -m "feat: add LlmConfig configuration key constants"
```

---

### Task 7: Replace LlmAttribute with Orleans facet-aware version

**Files:**
- Delete: `src/Core/LlmAttribute.cs`
- Create: `src/Core/AI/LlmAttribute.cs`

**Step 1: Delete old LlmAttribute.cs**

Delete `src/Core/LlmAttribute.cs` (the simple `[AttributeUsage(AttributeTargets.Class)] public sealed class LlmAttribute<TModel> : Attribute;`)

**Step 2: Create new LlmAttribute.cs in AI directory**

```csharp
namespace Core.AI;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
public abstract class LlmAttributeBase : Attribute, IFacetMetadata
{
    public abstract string ServiceKey { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : LlmAttributeBase where TModel : LLMModel
{
    private readonly Lazy<string> _serviceKey;
    public override string ServiceKey => _serviceKey.Value;

    public LlmAttribute()
    {
        _serviceKey = new Lazy<string>(() =>
        {
            var model = LLMModel.All.FirstOrDefault(m => m.GetType() == typeof(TModel))
                ?? throw new InvalidOperationException(
                    $"LLM model {typeof(TModel).Name} not found in registry.");
            return model.ServiceKey;
        });
    }
}
```

**Step 3: Update WeatherAgent.cs**

Change `using Core.AI.Models;` and update the attribute from `[Llm<OllamaLocal>]` to `[Llm<Llama32>]`:

```csharp
using Core.AI;
using Core.AI.Models;

namespace Core;

[Llm<Llama32>]
public class WeatherAgent : Agent
{
    public override string DisplayName => "Weather";
}
```

**Step 4: Update Agent.cs GetModelMarkerName()**

The `GetModelMarkerName()` method in `Agent.cs` already reflects on `LlmAttribute<>` generically, so it should continue to work. But update the check to use the new namespace. Find the method at line ~428 and ensure it works with `Core.AI.LlmAttribute<>`:

The existing reflection code checks for `typeof(LlmAttribute<>)`. Since we changed the namespace, add `using Core.AI;` to Agent.cs if not already present. The generic type definition check will resolve to the new type.

**Step 5: Delete old Models.cs**

Delete `src/Core/Models.cs` (the placeholder `Claude45Haiku`, `Claude45Sonnet`, `Gpt53`, `OllamaLocal` marker classes).

**Step 6: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success

**Step 7: Commit**

```bash
git add -A src/Core/
git commit -m "feat: replace placeholder LlmAttribute and Models with Orleans facet-aware versions"
```

---

### Task 8: Create LlmAttributeMapper for Orleans DI

**Files:**
- Create: `src/Core/AI/LlmAttributeMapper.cs`

**Step 1: Create LlmAttributeMapper.cs**

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Core.AI;

public sealed class LlmAttributeMapper<TModel>
    : IAttributeToFactoryMapper<LlmAttribute<TModel>>
    where TModel : LLMModel
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        LlmAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IChatClient))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type IChatClient.");

        return context =>
        {
            var chatClient = context.ActivationServices
                .GetKeyedService<IChatClient>(metadata.ServiceKey)
                ?? throw new InvalidOperationException(
                    $"LLM model '{typeof(TModel).Name}' not configured. " +
                    $"Service key: '{metadata.ServiceKey}'.");
            return chatClient;
        };
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success

**Step 3: Commit**

```bash
git add src/Core/AI/LlmAttributeMapper.cs
git commit -m "feat: add LlmAttributeMapper for Orleans IChatClient injection"
```

---

### Task 9: Create LlmRegistration for DI provider setup

**Files:**
- Create: `src/Core/AI/LlmRegistration.cs`

**Step 1: Create LlmRegistration.cs**

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace Core.AI;

public static class LlmRegistration
{
    public static IHostApplicationBuilder AddLlmProviders(this IHostApplicationBuilder builder)
    {
        LLMModel.EnsureAllModelsLoaded();
        var config = builder.Configuration;

        var declaredModels = ReadDeclaredModels(config);
        var modelsToRegister = declaredModels.Count > 0
            ? declaredModels
            : LLMModel.All.Where(m => IsProviderConfigured(config, m.Provider)).ToList();

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
                                   || !string.IsNullOrEmpty(config["ConnectionStrings:ollama"]),
            ProviderType.Anthropic => !string.IsNullOrEmpty(config[LlmConfig.AnthropicApiKey]),
            ProviderType.OpenAI => !string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]),
            _ => false
        };
    }

    internal static IChatClient CreateChatClient(IServiceProvider services, IConfiguration config, LLMModel model)
    {
        var innerClient = model.Provider switch
        {
            ProviderType.Ollama => CreateOllamaClient(config, model),
            ProviderType.Anthropic => CreateAnthropicClient(config, model),
            ProviderType.OpenAI => CreateOpenAiClient(config, model),
            _ => throw new NotSupportedException($"Provider {model.Provider} not supported")
        };

        return innerClient
            .AsBuilder()
            .UseOpenTelemetry(
                loggerFactory: services.GetService<ILoggerFactory>(),
                sourceName: "Core.Agent",
                configure: telemetry => telemetry.EnableSensitiveData = true)
            .Build(services);
    }

    private static IChatClient CreateOllamaClient(IConfiguration config, LLMModel model)
    {
        var endpoint = config[LlmConfig.OllamaEndpoint]
            ?? config["ConnectionStrings:ollama"]
            ?? "http://localhost:11434";
        return new OllamaApiClient(new Uri(endpoint), model.Id);
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
        var client = new OpenAI.OpenAIClient(apiKey);
        return client.GetChatClient(model.Id).AsIChatClient();
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Success

**Step 3: Commit**

```bash
git add src/Core/AI/LlmRegistration.cs
git commit -m "feat: add LlmRegistration with AddLlmProviders host builder extension"
```

---

### Task 10: Add WithLLM and WithLLMEnvironment to AppHost IAWExtensions

**Files:**
- Modify: `src/IAW.AppHost/IAWExtensions.cs`

**Step 1: Update IAWExtensions.cs**

Replace the entire file content:

```csharp
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
            var anthropicKey = appBuilder.AddParameter("anthropic-api-key", secret: true);
            builder.WithEnvironment("AI__LLM__AnthropicApiKey", anthropicKey);
        }

        if (_declaredProviders.Contains(ProviderType.OpenAI))
        {
            var openaiKey = appBuilder.AddParameter("openai-api-key", secret: true);
            builder.WithEnvironment("AI__LLM__OpenAiApiKey", openaiKey);
        }

        return builder;
    }
}
```

**Step 2: Add Core project reference to AppHost**

The AppHost needs a reference to Core to access `Core.AI.LLMModel`. Add to `Aspire.csproj`:

```xml
<ProjectReference Include="..\Core\Core.csproj" />
```

**Step 3: Build to verify**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Success

**Step 4: Commit**

```bash
git add src/IAW.AppHost/
git commit -m "feat: add WithLLM and WithLLMEnvironment to AppHost extensions"
```

---

### Task 11: Update AppHost.cs to use WithLLM

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`

**Step 1: Update AppHost.cs**

Add `WithLLM` calls and `WithLLMEnvironment` to the samples project reference:

```csharp
using Aspire;
using Core.AI.Models;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>()
    .WithLLM<Claude45Haiku>();

var enableAiResources = builder.Configuration.GetValue("IAW:EnableAiResources", true);

var samples = builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-silo", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 11_111;
    })
    .WithEndpoint("orleans-gateway", endpoint =>
    {
        endpoint.IsProxied = false;
        endpoint.Port = 30_000;
    });

if (enableAiResources)
{
    var ollama = builder.AddOllama("ollama").WithOpenWebUI().WithGPUSupport().WithDataVolume();
    var qwen = ollama.AddModel("qwen2.5");

    builder.AddProject<Projects.DevUI>("devui")
        .WithReference(qwen)
        .WithReference(iaw.AsClient())
        .WaitFor(qwen)
        .WaitFor(samples)
        .WithEnvironment("IAW__Orleans__Gateways__0", samples.GetEndpoint("orleans-gateway"));
}

builder.Build().Run();
```

**Step 2: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: Success

**Step 3: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: wire up WithLLM<Sonnet46> and WithLLM<Claude45Haiku> in AppHost"
```

---

### Task 12: Build and run tests

**Step 1: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Success

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All existing tests pass

**Step 3: Final commit if any fixups needed**

If tests reveal issues, fix and commit with descriptive message.
