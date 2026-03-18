# LLM Agents

Every IAW agent is backed by an LLM via `IChatClient` from `Microsoft.Extensions.AI`. This page covers the LlmAgentBase class hierarchy, the 5 built-in model agents, the `[Llm<T>]` injection attribute, and how to add new models.

## LlmAgentBase Hierarchy

```
DurableGrain (Orleans.Journaling)
  +-- Agent (IAW.Core)
        |-- Concrete agents with [Llm<TModel>] injection
        |
        |-- PersonalAssistantAgent  [Llm<Sonnet46>]
        |-- ReviewerAgent           [Llm<Claude45Haiku>]
        |-- SelfImprovementAgent    [Llm<Sonnet46>]
        |-- PlanningAgent           [Llm<Claude45Haiku>]
        |-- NotificationAgent       [Llm<Claude45Haiku>]
        |-- KnowledgeAgent          [Llm<Sonnet46>]
        |-- UserAgent               [Llm<Claude45Haiku>]
        |-- RoslynAgent             [Llm<Claude45Haiku>]
        |-- DotNetAgent             [Llm<Claude45Haiku>]
        +-- ...
```

The `Agent` base class accepts an `IChatClient` constructor parameter. On activation, it wraps this client with streaming usage tracking and optional function invocation middleware. Derived agents specify which model to use via the `[Llm<T>]` attribute.

## LLMModel Class

Every model is defined as a singleton extending the `LLMModel` class:

```csharp
namespace IAW.Core.AI;

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
}
```

The `ServiceKey` is used for keyed DI registration. It combines the provider name with a normalized model ID (e.g., `anthropic-claude-sonnet-4-6`).

### ProviderType

```csharp
public enum ProviderType
{
    Ollama,     // local models
    Anthropic,  // Claude models
    OpenAI      // GPT models
}
```

### ModelCapabilities

```csharp
public sealed record ModelCapabilities(
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsStreaming,
    bool SupportsStructuredOutput)
{
    public static ModelCapabilities FullyCapable =>
        new(true, true, true, true);
    public static ModelCapabilities ChatOnly =>
        new(false, false, true, false);
    public static ModelCapabilities ToolCapable =>
        new(true, false, true, true);
}
```

## Built-in Models

IAW ships with 5 model definitions:

### Anthropic Models

```csharp
// Sonnet 4.6 -- high-quality reasoning, code generation
public sealed class Sonnet46 : LLMModel
{
    public static readonly Sonnet46 Instance = new();
    private Sonnet46() { }

    public override string Id => "claude-sonnet-4-6";
    public override string DisplayName => "Claude Sonnet 4.6";
    public override ProviderType Provider => ProviderType.Anthropic;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

// Claude 4.5 Haiku -- fast, cost-effective
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

### OpenAI Models

```csharp
// GPT-4o -- fully capable, balanced performance
public sealed class Gpt4o : LLMModel
{
    public static readonly Gpt4o Instance = new();
    public override string Id => "gpt-4o";
    public override string DisplayName => "GPT-4o";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

// GPT-4o Mini -- smaller, faster, cheaper
public sealed class Gpt4oMini : LLMModel
{
    public static readonly Gpt4oMini Instance = new();
    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GPT-4o Mini";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
```

### Ollama (Local) Models

```csharp
// Llama 3.2 -- local model via Ollama, chat only
public sealed class Llama32 : LLMModel
{
    public static readonly Llama32 Instance = new();
    public override string Id => "llama3.2";
    public override string DisplayName => "Llama 3.2";
    public override ProviderType Provider => ProviderType.Ollama;
    public override ModelCapabilities Capabilities => ModelCapabilities.ChatOnly;
}
```

## Model Summary

| Model | Provider | Capabilities | Typical Use |
|---|---|---|---|
| `Sonnet46` | Anthropic | Fully capable | Complex reasoning, orchestration, code generation |
| `Claude45Haiku` | Anthropic | Fully capable | Fast tasks, tools, CI/CD agents |
| `Gpt4o` | OpenAI | Fully capable | General purpose, broad knowledge |
| `Gpt4oMini` | OpenAI | Tool capable | Lightweight tasks, classification |
| `Llama32` | Ollama | Chat only | Local development, privacy-sensitive tasks |

## [Llm&lt;T&gt;] Attribute

The `[Llm<T>]` attribute is a constructor parameter attribute that resolves a keyed `IChatClient` from DI:

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : LlmAttributeBase
    where TModel : LLMModel
{
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

### LlmAttributeMapper

Orleans resolves the attribute via `LlmAttributeMapper<TModel>`, which implements `IAttributeToFactoryMapper`:

```csharp
public sealed class LlmAttributeMapper<TModel>
    : IAttributeToFactoryMapper<LlmAttribute<TModel>>
    where TModel : LLMModel
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter, LlmAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IChatClient))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type IChatClient.");

        return context =>
        {
            var chatClient = context.ActivationServices
                .GetKeyedService<IChatClient>(metadata.ServiceKey)
                ?? throw new InvalidOperationException(
                    $"LLM model '{typeof(TModel).Name}' not configured.");
            return chatClient;
        };
    }
}
```

This means Orleans looks up the `IChatClient` by the model's `ServiceKey` from the DI container at grain activation time.

### Usage

```csharp
public class MyAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Sonnet46>] IChatClient chatClient)   // <-- model injection
    : Agent(state, eventLog, trackingItems, chatClient), IMyAgent
{
    protected override string DisplayName => "My Agent";
    protected override string SystemPrompt => "You are a helpful agent.";
}
```

## Grain Interfaces for Models

Each model has an `IAgent`-extending interface for typed grain resolution:

```csharp
namespace IAW.Core.AI.Models;

public interface ISonnet46 : IAgent { }
public interface IClaude45Haiku : IAgent { }
public interface IGpt4o : IAgent { }
public interface IGpt4oMini : IAgent { }
public interface ILlama32 : IAgent { }
```

These interfaces let you resolve agents that use a specific model:

```csharp
var sonnetAgent = GrainFactory.GetGrain<ISonnet46>("my-sonnet-agent");
var haikuAgent = GrainFactory.GetGrain<IClaude45Haiku>("my-haiku-agent");
```

## Adding a New Model

Adding a new model requires three files in `src/Core/AI/Models/`:

### Step 1: Create the LLMModel singleton

```csharp
// src/Core/AI/Models/Gpt52.cs
namespace IAW.Core.AI.Models;

public sealed class Gpt52 : LLMModel
{
    public static readonly Gpt52 Instance = new();
    private Gpt52() { }

    public override string Id => "gpt-5.2";
    public override string DisplayName => "GPT-5.2";
    public override ProviderType Provider => ProviderType.OpenAI;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
```

### Step 2: Create the grain interface

```csharp
// src/Core/AI/Models/IGpt52.cs
namespace IAW.Core.AI.Models;

public interface IGpt52 : IAgent { }
```

### Step 3: Register in EnsureAllModelsLoaded

Add the new model to the static initializer in `LLMModel`:

```csharp
public static void EnsureAllModelsLoaded()
{
    _ = Models.Claude45Haiku.Instance;
    _ = Models.Sonnet46.Instance;
    _ = Models.Gpt4o.Instance;
    _ = Models.Gpt4oMini.Instance;
    _ = Models.Llama32.Instance;
    _ = Models.Gpt52.Instance;  // new model
}
```

### Step 4: Use in an agent

```csharp
public class AdvancedReasoningAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Gpt52>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), IGpt52
{
    protected override string DisplayName => "Advanced Reasoning";
    protected override string SystemPrompt => "You solve complex problems.";
}
```

### Step 5: Register in AppHost

```csharp
var iaw = builder.AddIAW()
    .WithLLM<Gpt52>();
```

### Adding a New Provider

If you need a provider other than Anthropic, OpenAI, or Ollama:

1. Add a new value to the `ProviderType` enum
2. Update the LLM registration code in `LlmRegistration.cs` to handle the new provider
3. Update `IAWExtensions.WithLLMEnvironment` to inject the API key for the new provider

## Activation Flow

When an agent grain activates:

1. Orleans resolves the `[Llm<T>]` parameter via `LlmAttributeMapper<T>`
2. The mapper looks up a keyed `IChatClient` from `context.ActivationServices`
3. The `Agent` base class wraps the client:
   - Adds streaming usage tracking via `UseStreamingUsage()`
   - Adds function invocation middleware if the agent defines tools
4. The wrapped client is stored as `_toolClient` and used for all LLM calls
5. `SendMessage` builds the chat history, injects context provider data, and streams the response

```csharp
// In Agent.Lifecycle.cs
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);

    var tools = GetAllTools();
    var builder = new ChatClientBuilder(chatClient)
        .UseStreamingUsage();

    if (tools.Count > 0)
        builder.UseFunctionInvocation();

    _toolClient = builder.Build();

    await SubscribeToStreamConsumerInterfaces();
}
```
