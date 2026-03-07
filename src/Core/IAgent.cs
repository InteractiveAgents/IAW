using Microsoft.Extensions.AI;

namespace IAW.Core;

public interface IAgent : IGrainWithStringKey
{
    // Conversation
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct);
    Task<string> GetResponse(string prompt, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken ct);
    Task ClearHistoryAsync(CancellationToken ct);

    // State
    Task<AgentState> GetStateAsync(CancellationToken ct);
    Task SetWorkspaceAsync(string path, CancellationToken ct);

    // Metadata
    Task<AgentMetadata> GetMetadataAsync(CancellationToken ct);
    Task<AgentCapabilities> GetCapabilitiesAsync(CancellationToken ct);

    // Events
    Task HandleEventAsync(AgentEvent agentEvent, CancellationToken ct);
    Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct);

    // Streams
    Task PublishToStreamAsync(AgentEvent evt, CancellationToken ct);
    Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(CancellationToken ct);

    // Lifecycle
    Task CancelAsync(CancellationToken ct);
}
