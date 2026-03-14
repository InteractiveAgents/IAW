using System.Diagnostics;
using System.Runtime.CompilerServices;
using Core.Agents;
using Core.AI;
using Core.Contracts;
using Core.Services;
using ChatMessage = Core.Contracts.ChatMessage;
using Core.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Streams;

namespace IAW.Core;

[GrainType("agent-v3")]
public abstract partial class Agent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : DurableGrain, IAgent
{
    private readonly UsageCaptureChatClient _usageCapture = new(chatClient);
    private AIAgent? _agent;
    private AgentSession? _session;
    private IReadOnlyList<ContentPart>? _currentMessageParts;

    protected virtual string Instructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
    protected virtual int MaxHistoryMessages => 100;
    protected IChatClient ChatClient => chatClient;
    protected IDurableList<ChatMessage> History => durableState.History;
    protected IDurableDictionary<string, StateEntry> State => durableState.State;
    protected IDurableList<AgentEvent> EventLog => durableState.EventLog;
    protected IStreamProvider StreamProvider => this.GetStreamProvider("agents");
    protected virtual IReadOnlyList<global::Core.Context.IAgentContextProvider> GetContextProviders() => [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.activate");
        activity?.SetTag("agent.type", GetType().Name);
        activity?.SetTag("agent.id", this.GetPrimaryKeyString());
        AgentTelemetry.Activations.Add(1, new TagList { { "agent.type", GetType().Name } });

        var blobStorage = ServiceProvider.GetService<BlobFileStorage>();
        _agent = _usageCapture.AsAIAgent(new ChatClientAgentOptions
        {
            Name = this.GetPrimaryKeyString(),
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = [.. GetAllTools()]
            },
            ChatHistoryProvider = new DurableChatHistoryProvider(durableState.History, MaxHistoryMessages, blobStorage)
        });

        _session = await _agent.CreateSessionAsync(cancellationToken);

        await SubscribeToStreamConsumerInterfaces();

        foreach (var kvp in durableState.TrackingItems)
            await this.RegisterOrUpdateReminder(kvp.Key, TimeSpan.Zero, kvp.Value.Interval);

        await base.OnActivateAsync(cancellationToken);
    }

    public IAsyncEnumerable<string> GetResponseStream(
        string prompt,
        CancellationToken cancellationToken)
    {
        var message = new ChatMessage
        {
            Role = "user",
            Content = prompt,
            Parts = new List<ContentPart> { new global::Core.Contracts.TextContent(prompt) }
        };
        return GetResponseStream(message, cancellationToken);
    }

    public IAsyncEnumerable<string> GetResponseStream(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });
        _currentMessageParts = message.Parts;
        return StreamResponseCore(message.Text, cancellationToken);
    }

    private async IAsyncEnumerable<string> StreamResponseCore(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity(
            $"invoke_agent {this.GetPrimaryKeyString()}", ActivityKind.Server);
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.provider.name", "iaw");
        activity?.SetTag("gen_ai.agent.id", this.GetPrimaryKeyString());
        activity?.SetTag("gen_ai.agent.name", DisplayName);
        activity?.SetTag("gen_ai.conversation.id", this.GetPrimaryKeyString());

        var sw = Stopwatch.StartNew();
        var completed = false;
        try
        {
            prompt = await EnrichWithContext(prompt, cancellationToken);

            await foreach (var chunk in _agent!.RunStreamingAsync(prompt, _session, cancellationToken: cancellationToken))
            {
                if (chunk.Text is not { } text)
                    continue;
                yield return text;
                Activity.Current = activity; // restore after yield (dotnet/runtime#47802)
            }

            if (_usageCapture.LastUsage is { } usage)
            {
                activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
                activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
                RecordTokenMetrics(usage);
            }

            var correlationId = activity?.TraceId.ToString() ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
            durableState.EventLog.Add(new AgentEvent(
                "LlmCall", this.GetPrimaryKeyString(), correlationId,
                DateTimeOffset.UtcNow, new Dictionary<string, object> { ["prompt_length"] = prompt.Length }));

            await WriteStateAsync(cancellationToken);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                activity?.SetTag("error.type", "conversation_error");
                AgentTelemetry.ConversationErrors.Add(1, new TagList { { "agent.type", GetType().Name } });
            }
            AgentTelemetry.ConversationDuration.Record(sw.Elapsed.TotalSeconds,
                new TagList { { "agent.type", GetType().Name } });
        }
    }

    public async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in GetResponseStream(prompt, cancellationToken))
            sb.Append(chunk);
        return sb.ToString();
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChatMessage> snapshot = durableState.History.ToList();
        return Task.FromResult(snapshot);
    }

    public async Task ClearHistory(CancellationToken cancellationToken = default)
    {
        durableState.History.Clear();
        await WriteStateAsync(cancellationToken);
        _session = await _agent!.CreateSessionAsync(cancellationToken);
    }

    public Task<AgentUsage?> GetLastUsage(CancellationToken ct = default)
        => Task.FromResult(_usageCapture.LastUsage);

    private void RecordTokenMetrics(AgentUsage usage)
    {
        var tags = new TagList
        {
            { "gen_ai.agent.name", DisplayName },
            { "gen_ai.operation.name", "invoke_agent" }
        };
        var inputTags = new TagList
        {
            { "gen_ai.agent.name", DisplayName },
            { "gen_ai.operation.name", "invoke_agent" },
            { "gen_ai.token.type", "input" }
        };
        var outputTags = new TagList
        {
            { "gen_ai.agent.name", DisplayName },
            { "gen_ai.operation.name", "invoke_agent" },
            { "gen_ai.token.type", "output" }
        };
        AgentTelemetry.TokenUsage.Record(usage.InputTokens, inputTags);
        AgentTelemetry.TokenUsage.Record(usage.OutputTokens, outputTags);
        AgentTelemetry.TotalInputTokens.Add(usage.InputTokens, tags);
        AgentTelemetry.TotalOutputTokens.Add(usage.OutputTokens, tags);

        var currentInput = GetLongFromState("cumulative-input-tokens");
        var currentOutput = GetLongFromState("cumulative-output-tokens");
        durableState.State["cumulative-input-tokens"] = new StateEntry("cumulative-input-tokens", currentInput + usage.InputTokens);
        durableState.State["cumulative-output-tokens"] = new StateEntry("cumulative-output-tokens", currentOutput + usage.OutputTokens);
    }

    private long GetLongFromState(string key)
    {
        if (!durableState.State.TryGetValue(key, out var entry)) return 0;
        return entry.Value is long l ? l : long.TryParse(entry.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private async Task<string> EnrichWithContext(string prompt, CancellationToken ct)
    {
        var providers = GetContextProviders();
        if (providers.Count == 0) return prompt;

        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.enrich_context");
        activity?.SetTag("context.provider_count", providers.Count);

        var contextParts = new List<string>();
        foreach (var provider in providers)
        {
            try
            {
                var items = await provider.GetContextAsync(this.GetPrimaryKeyString(), prompt, ct);
                contextParts.AddRange(items);
            }
            catch
            {
                // context provider unavailable — skip
            }
        }

        activity?.SetTag("context.items_found", contextParts.Count);

        if (contextParts.Count == 0) return prompt;

        return $"[Relevant context from memory]\n{string.Join("\n", contextParts)}\n\n[User message]\n{prompt}";
    }

    protected static string BuildSafeErrorMessage(Exception ex)
        => $"An error occurred: {ex.GetType().Name} — {ex.Message}";
}
