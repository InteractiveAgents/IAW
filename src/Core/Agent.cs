using System.Diagnostics;
using System.Runtime.CompilerServices;
using IAW.Core.Observability;
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
    private AIAgent? _agent;
    private AgentSession? _session;

    protected virtual string Instructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
    protected IChatClient ChatClient => chatClient;
    protected IDurableList<ChatMessage> History => history;
    protected IDurableDictionary<string, StateEntry> State => state;
    protected IDurableList<AgentEvent> EventLog => eventLog;
    protected IStreamProvider StreamProvider => this.GetStreamProvider("agents");

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.activate");
        activity?.SetTag("agent.type", GetType().Name);
        activity?.SetTag("agent.id", this.GetPrimaryKeyString());
        AgentTelemetry.Activations.Add(1, new TagList { { "agent.type", GetType().Name } });

        _agent = chatClient.AsAIAgent(new ChatClientAgentOptions
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

        await base.OnActivateAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> GetResponseStream(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        AgentTelemetry.MessagesSent.Add(1, new TagList { { "agent.type", GetType().Name } });

        await foreach (var chunk in _agent!.RunStreamingAsync(prompt, _session, cancellationToken: cancellationToken))
        {
            if (chunk.Text is not { } text)
                continue;

            yield return text;
        }

        await WriteStateAsync(cancellationToken);
    }

    public async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
    {
        var response = await _agent!.RunAsync(prompt, _session, cancellationToken: cancellationToken);
        await WriteStateAsync(cancellationToken);
        return response.Text ?? string.Empty;
    }

    public Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ChatMessage> snapshot = history.ToList();
        return Task.FromResult(snapshot);
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        history.Clear();
        await WriteStateAsync(cancellationToken);
        _session = await _agent!.CreateSessionAsync(cancellationToken);
    }

    protected static string BuildSafeErrorMessage(Exception ex)
        => $"An error occurred: {ex.GetType().Name} — {ex.Message}";
}