using Microsoft.Extensions.AI;

namespace IAW.Core;

public interface IAgent : IGrainWithStringKey
{
    // Conversation
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct);
    Task<string> GetResponse(string prompt, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken ct);
    Task ClearHistory(CancellationToken ct);

    // State
    Task<AgentState> GetState(CancellationToken ct);
    Task SetWorkspace(string path, CancellationToken ct);

    // Metadata
    Task<AgentMetadata> GetMetadata(CancellationToken ct);
    Task<AgentCapabilities> GetCapabilities(CancellationToken ct);

    // Events
    Task HandleEvent(AgentEvent agentEvent, CancellationToken ct);
    Task<IReadOnlyList<AgentEvent>> GetEventLog(CancellationToken ct);

    // Streams
    Task PublishToStream(AgentEvent evt, CancellationToken ct);
    Task<IReadOnlyList<string>> GetActiveSubscriptions(CancellationToken ct);

    // Lifecycle
    Task Cancel(CancellationToken ct);
}