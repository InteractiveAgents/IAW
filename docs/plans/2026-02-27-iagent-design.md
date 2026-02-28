# IAgent Design — Agent Base Class with Built-in Behaviors

## Core Decisions

- **Thin `IAgent` interface** — just `Id`, `DisplayName`, `SendAsync` for external callers
- **Fat `Agent` base class** — all behaviors built-in, one class to rule them all
- **No base constructor** — LLM injected via `[Llm<T>]` attribute, property-injected by hosting framework
- **Wraps `AIAgent` internally** — delegates LLM calls to Microsoft.Agents.AI's `AIAgent` via `.AsAIAgent()`
- **Streaming by default** — `SendAsync` returns `IAsyncEnumerable<string>`
- **Model markers** — empty types (`Claude45Haiku`, `Gpt53`, `Ollama`) as generic args to `[Llm<T>]`

## Core Shape (Step 1)

```csharp
namespace Core;

public interface IAgent
{
    string Id { get; }
    string DisplayName { get; }
    IAsyncEnumerable<string> SendAsync(string message, CancellationToken ct = default);
}

public class Agent : IAgent
{
    public string Id => GetType().Name;
    public virtual string DisplayName => Id;
    protected AIAgent? Llm { get; internal set; }

    public virtual async IAsyncEnumerable<string> SendAsync(
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Llm is null) yield break;
        await foreach (var update in Llm.RunStreamingAsync(message, cancellationToken: ct))
            yield return update.ToString();
    }
}

public sealed class Claude45Haiku;
public sealed class Gpt53;
public sealed class Ollama;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LlmAttribute<TModel> : Attribute;

[Llm<Claude45Haiku>]
public class WeatherAgent : Agent
{
    public override string DisplayName => "Weather";
}
```

## Migration Roadmap

Each step adds to Agent. No step changes previous steps.

| Step | Behavior | Adds to Agent | Depends on |
|------|----------|---------------|------------|
| 1 | Conversations + LLM | `SendAsync`, `[Llm<T>]`, `AIAgent` wrapping | — |
| 2 | System Prompt + Identity | `virtual string SystemPrompt`, pass to `AsAIAgent(instructions:)` | 1 |
| 3 | Conversation History | `AgentSession` management, `GetHistoryAsync()` | 1 |
| 4 | Tools | `virtual IReadOnlyList<AITool> DefineTools()`, auto-register | 1 |
| 5 | Events | `PublishEventAsync()`, `HandleEventAsync()`, event log | 1 |
| 6 | Notifications | `NotifyAsync()`, `SubscribeAsync()` between agents | 5 |
| 7 | State | `Dictionary<string, object>` state bag, `GetStateAsync()` | 1 |
| 8 | Metadata | `GetMetadataAsync()` — auto-discovers `[Llm<T>]`, capabilities | 1-7 |
| 9 | Tracking | `StartTrackingAsync()`, periodic checks with intervals | 4 |
| 10 | Streaming | Pub/sub stream channels between agents | 5 |
| 11 | Observability | OpenTelemetry, metrics, `DiagnoseAsync()` | 1-8 |
| 12 | Dynamic Config | `ConfigureAsync()` to change agent behavior at runtime | 2 |

## Constraints

- Each step is a separate migration, confirmed by user before proceeding
- No composable behavior interfaces — Agent is the god object
- `[Llm<T>]` is the only mechanism for LLM access — no constructor injection
- Microsoft.Agents.AI is wrapped, not inherited from
