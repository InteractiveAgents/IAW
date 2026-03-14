using Orleans.Runtime;

namespace Core.Contracts;

public interface IAgent : IGrainWithStringKey
{
    // Conversation
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct);
    IAsyncEnumerable<string> GetResponseStream(ChatMessage message, CancellationToken ct);

    [ResponseTimeout("00:05:00")]
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
    Task<IReadOnlyList<AgentEvent>> GetEventLog(CancellationToken ct);

    // Streams
    Task<IReadOnlyList<string>> GetActiveSubscriptions(CancellationToken ct);

    // Usage
    Task<AgentUsage?> GetLastUsage(CancellationToken ct);

    // Lifecycle
    Task Cancel(CancellationToken ct);
}