using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Core.V3;

[GrainType("agent-v3")]
public abstract partial class Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history)
    : DurableGrain, IAgent
{
    private AIAgent? _agent;
    private AgentSession? _session;

    protected virtual string Instructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
    protected IDurableList<ChatMessage> History => history;

    protected IDurableDictionary<string, StateEntry> State => state;

    protected IDurableList<AgentEvent> EventLog => eventLog;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
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
        
        await base.OnActivateAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> GetResponseStream(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
        IReadOnlyList<ChatMessage> snapshot = [.. history];
        return Task.FromResult(snapshot);
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        history.Clear();
        await WriteStateAsync(cancellationToken);
        _session = await _agent!.CreateSessionAsync(cancellationToken);
    }

    public virtual Task HandleEventAsync(AgentEvent agentEvent, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AgentEvent>>(eventLog.ToList());

    public async Task PublishToStreamAsync(AgentEvent evt, CancellationToken ct = default)
    {
        eventLog.Add(evt);
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(new List<string>());
}