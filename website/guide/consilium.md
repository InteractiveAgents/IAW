# Consilium: Multi-Model Patterns

IAW supports multiple LLM providers and models simultaneously. The Consilium patterns let you route tasks to the best model, combine outputs from multiple models, and take consensus votes. This page covers adaptive routing, majority vote, synthesis, and how to implement them with IAW's LLM agent infrastructure.

## Available Models

IAW ships with 5 built-in model definitions. Each is a singleton extending `LLMModel`:

| Class | Model ID | Provider | Capabilities |
|---|---|---|---|
| `Sonnet46` | `claude-sonnet-4-6` | Anthropic | Fully capable |
| `Claude45Haiku` | `claude-haiku-4-5-20251001` | Anthropic | Fully capable |
| `Gpt4o` | `gpt-4o` | OpenAI | Fully capable |
| `Gpt4oMini` | `gpt-4o-mini` | OpenAI | Tool capable |
| `Llama32` | `llama3.2` | Ollama | Chat only |

Each model declares its capabilities:

```csharp
public sealed record ModelCapabilities(
    bool SupportsTools,
    bool SupportsVision,
    bool SupportsStreaming,
    bool SupportsStructuredOutput);

// Presets
ModelCapabilities.FullyCapable  // all true
ModelCapabilities.ToolCapable   // tools + streaming + structured, no vision
ModelCapabilities.ChatOnly      // streaming only
```

## Model-Specific Agents

Each model has a corresponding grain interface (e.g., `ISonnet46`, `IGpt4o`). You create model-specific agents by injecting the model's `IChatClient` via the `[Llm<T>]` attribute:

```csharp
using IAW.Core.AI;
using IAW.Core.AI.Models;

public interface ICodeAnalyzer : IAgent;

public class CodeAnalyzerAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), ICodeAnalyzer
{
    protected override string DisplayName => "Code Analyzer";
    protected override string SystemPrompt => "You analyze C# code for quality issues.";
}
```

The `[Llm<Sonnet46>]` attribute resolves to a keyed `IChatClient` registered in DI with the model's service key (e.g., `anthropic-claude-sonnet-4-6`).

## Adaptive Routing

Adaptive routing picks the best model for each task based on complexity, cost constraints, or required capabilities.

### Router Agent

Create a router agent that analyzes incoming requests and delegates to the appropriate model-specific agent:

```csharp
public interface IAdaptiveRouter : IAgent;

public class AdaptiveRouterAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), IAdaptiveRouter
{
    protected override string DisplayName => "Adaptive Router";
    protected override string SystemPrompt =>
        "You route tasks to the best model. Use RouteTask to classify and delegate.";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(RouteTask)
    ];

    [Description("Route a task to the best model based on complexity")]
    private async Task<string> RouteTask(
        [Description("The task to route")] string task,
        [Description("Complexity: simple, moderate, complex")] string complexity)
    {
        var agentKey = complexity switch
        {
            "simple" => "haiku-worker",     // Claude 4.5 Haiku -- fast, cheap
            "moderate" => "gpt4o-worker",   // GPT-4o -- balanced
            "complex" => "sonnet-worker",   // Sonnet 4.6 -- highest quality
            _ => "haiku-worker"
        };

        var agent = GrainFactory.GetGrain<IAgent>(agentKey);
        var message = new ChatMessage(task, ChatRole.User);
        var responseBuilder = new StringBuilder();

        await foreach (var response in agent.SendMessage(message, AgentCancellation))
        {
            if (response.Kind == AgentResponseKind.Text)
                responseBuilder.Append(response.Content);
        }

        return responseBuilder.ToString();
    }
}
```

### Capability-Based Routing

Route based on what the task requires:

```csharp
private string SelectModel(bool needsVision, bool needsTools, bool needsStructuredOutput)
{
    // Check model capabilities against requirements
    foreach (var model in LLMModel.All)
    {
        var caps = model.Capabilities;
        if (needsVision && !caps.SupportsVision) continue;
        if (needsTools && !caps.SupportsTools) continue;
        if (needsStructuredOutput && !caps.SupportsStructuredOutput) continue;
        return model.ServiceKey;
    }
    return LLMModel.All.First().ServiceKey;
}
```

## Majority Vote

Ask multiple models the same question and take the consensus answer. This improves reliability for factual queries, code review, and classification tasks.

```csharp
public interface IVotingAgent : IAgent;

public class VotingAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), IVotingAgent
{
    protected override string DisplayName => "Majority Vote";
    protected override string SystemPrompt => "You aggregate votes from multiple models.";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(MajorityVote)
    ];

    [Description("Ask multiple models and return the consensus")]
    private async Task<string> MajorityVote(
        [Description("The question to ask")] string question)
    {
        var agentKeys = new[] { "voter-sonnet", "voter-gpt4o", "voter-haiku" };
        var responses = new List<string>();

        // Query all models in parallel
        var tasks = agentKeys.Select(async key =>
        {
            var agent = GrainFactory.GetGrain<IAgent>(key);
            var message = new ChatMessage(
                $"Answer concisely in one sentence: {question}", ChatRole.User);
            var sb = new StringBuilder();
            await foreach (var r in agent.SendMessage(message, AgentCancellation))
            {
                if (r.Kind == AgentResponseKind.Text) sb.Append(r.Content);
            }
            return sb.ToString();
        });

        responses.AddRange(await Task.WhenAll(tasks));

        // Use the coordinator model to find consensus
        var votePrompt = $"""
            Three models answered this question: "{question}"

            Model 1 (Sonnet): {responses[0]}
            Model 2 (GPT-4o): {responses[1]}
            Model 3 (Haiku): {responses[2]}

            Identify the majority consensus. If all three disagree,
            pick the most well-reasoned answer. Return only the consensus answer.
            """;

        var history = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, votePrompt)
        };
        var consensus = await ChatClient.GetResponseAsync(history);
        return consensus.Text ?? responses[0];
    }
}
```

### Vote Tracking

Store vote results in durable state for analysis:

```csharp
State[$"vote-{Guid.NewGuid():N}"[..12]] = new StateDescriptor("vote",
    JsonSerializer.Serialize(new
    {
        Question = question,
        Responses = responses,
        Consensus = consensus.Text,
        Timestamp = DateTimeOffset.UtcNow,
        Agreement = responses.Count(r =>
            r.Contains(consensus.Text ?? "", StringComparison.OrdinalIgnoreCase))
    }));
await WriteStateAsync(AgentCancellation);
```

## Synthesis

Synthesis combines outputs from multiple models into a single, richer response. Each model contributes its strengths -- one may excel at code analysis, another at documentation, a third at edge cases.

```csharp
public interface ISynthesisAgent : IAgent;

public class SynthesisAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), ISynthesisAgent
{
    protected override string DisplayName => "Synthesis";
    protected override string SystemPrompt =>
        "You synthesize outputs from multiple AI models into comprehensive results.";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(SynthesizeReview)
    ];

    [Description("Get code review from multiple models and synthesize")]
    private async Task<string> SynthesizeReview(
        [Description("Code to review")] string code)
    {
        // Specialist 1: Architecture review (Sonnet -- deep reasoning)
        var architectureReview = await QueryAgent("synthesis-sonnet",
            $"Review this code for architectural issues only:\n{code}");

        // Specialist 2: Security review (GPT-4o -- broad knowledge)
        var securityReview = await QueryAgent("synthesis-gpt4o",
            $"Review this code for security vulnerabilities only:\n{code}");

        // Specialist 3: Performance review (Haiku -- fast, focused)
        var performanceReview = await QueryAgent("synthesis-haiku",
            $"Review this code for performance issues only:\n{code}");

        // Synthesize with the primary model
        var synthesisPrompt = $"""
            Combine these three specialist reviews into a single comprehensive review.
            Prioritize by severity. Remove duplicates. Add cross-cutting insights.

            Architecture Review:
            {architectureReview}

            Security Review:
            {securityReview}

            Performance Review:
            {performanceReview}
            """;

        var history = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.User, synthesisPrompt)
        };
        var result = await ChatClient.GetResponseAsync(history);
        return result.Text ?? "Synthesis failed";
    }

    private async Task<string> QueryAgent(string agentKey, string prompt)
    {
        var agent = GrainFactory.GetGrain<IAgent>(agentKey);
        var message = new ChatMessage(prompt, ChatRole.User);
        var sb = new StringBuilder();
        await foreach (var r in agent.SendMessage(message, AgentCancellation))
        {
            if (r.Kind == AgentResponseKind.Text) sb.Append(r.Content);
        }
        return sb.ToString();
    }
}
```

## AppHost Configuration

To use multiple models, register them in your AppHost:

```csharp
using IAW.Core.AI.Models;
using IAW.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW()
    .WithLLM<Sonnet46>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Gpt4o>()
    .WithLLM<Gpt4oMini>();

var silo = builder.AddProject<Projects.IAW_Silo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);
```

Each `WithLLM<T>()` call registers the model in the IAW configuration. `WithLLMEnvironment` injects the model declarations and API keys as environment variables into the silo project.

The environment variables follow this pattern:
- `AI__LLM__Models__0__Id` -- model ID (e.g., `claude-sonnet-4-6`)
- `AI__LLM__Models__0__Provider` -- provider type (e.g., `Anthropic`)
- `AI__LLM__Models__0__ServiceKey` -- DI service key
- `AI__LLM__AnthropicApiKey` -- Anthropic API key (from Aspire parameters)
- `AI__LLM__OpenAiApiKey` -- OpenAI API key (from Aspire parameters)

## Pattern Comparison

| Pattern | Models Used | Latency | Cost | Best For |
|---|---|---|---|---|
| Adaptive Routing | 1 per request | Low | Optimized | Varying task complexity |
| Majority Vote | 3+ per request | Highest (parallel) | High | Factual accuracy, classification |
| Synthesis | 3+ per request | High (parallel + merge) | Highest | Comprehensive analysis |

For most applications, adaptive routing provides the best cost-to-quality ratio. Use majority vote when correctness is critical. Use synthesis when you need depth across multiple dimensions.
