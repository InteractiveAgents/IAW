using System.Diagnostics;
using System.Runtime.CompilerServices;
using Core.AI;
using Core.Contracts;
using ChatMessage = Core.Contracts.ChatMessage;
using Core.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace IAW.Core;

[GrainType("agent-v3")]
public abstract partial class Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : DurableGrain, IAgent
{
    private readonly UsageCaptureChatClient _usageCapture = new(chatClient);
    private AIAgent? _agent;
    private AgentSession? _session;

    protected virtual string Instructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
    protected IChatClient ChatClient => chatClient;
    protected IDurableList<ChatMessage> History => history;
    protected IDurableDictionary<string, StateEntry> State => state;
    protected IDurableList<AgentEvent> EventLog => eventLog;
    protected IStreamProvider StreamProvider => this.GetStreamProvider("agents");
    protected virtual IReadOnlyList<global::Core.Context.IAgentContextProvider> GetContextProviders() => [];

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.activate");
        activity?.SetTag("agent.type", GetType().Name);
        activity?.SetTag("agent.id", this.GetPrimaryKeyString());
        AgentTelemetry.Activations.Add(1, new TagList { { "agent.type", GetType().Name } });

        _agent = _usageCapture.AsAIAgent(new ChatClientAgentOptions
        {
            Name = this.GetPrimaryKeyString(),
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = [.. GetAllTools()]
            },
            ChatHistoryProvider = new DurableChatHistoryProvider(history)
        });

        _session = await _agent.CreateSessionAsync(cancellationToken);

        await SubscribeToStreamConsumerInterfaces();

        foreach (var kvp in trackingItems)
            await this.RegisterOrUpdateReminder(kvp.Key, TimeSpan.Zero, kvp.Value.Interval);

        await base.OnActivateAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> GetResponseStream(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });

        prompt = await EnrichWithContext(prompt, cancellationToken);
        await foreach (var chunk in _agent!.RunStreamingAsync(prompt, _session, cancellationToken: cancellationToken))
        {
            if (chunk.Text is not { } text)
                continue;

            yield return text;
        }

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        eventLog.Add(new AgentEvent(
            "LlmStreamCall", this.GetPrimaryKeyString(), correlationId,
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["prompt_length"] = prompt.Length }));

        await WriteStateAsync(cancellationToken);
    }

    public async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
    {
        prompt = await EnrichWithContext(prompt, cancellationToken);
        var response = await _agent!.RunAsync(prompt, _session, cancellationToken: cancellationToken);

        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
        eventLog.Add(new AgentEvent(
            "LlmCall", this.GetPrimaryKeyString(), correlationId,
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["prompt_length"] = prompt.Length }));

        await WriteStateAsync(cancellationToken);
        return response.Text ?? string.Empty;
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChatMessage> snapshot = history.ToList();
        return Task.FromResult(snapshot);
    }

    public async Task ClearHistory(CancellationToken cancellationToken = default)
    {
        history.Clear();
        await WriteStateAsync(cancellationToken);
        _session = await _agent!.CreateSessionAsync(cancellationToken);
    }

    public Task<AgentUsage?> GetLastUsage(CancellationToken ct = default)
        => Task.FromResult(_usageCapture.LastUsage);

    private async Task<string> EnrichWithContext(string prompt, CancellationToken ct)
    {
        var providers = GetContextProviders();
        if (providers.Count == 0) return prompt;

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

        if (contextParts.Count == 0) return prompt;

        return $"[Relevant context from memory]\n{string.Join("\n", contextParts)}\n\n[User message]\n{prompt}";
    }

    protected static string BuildSafeErrorMessage(Exception ex)
        => $"An error occurred: {ex.GetType().Name} — {ex.Message}";
}