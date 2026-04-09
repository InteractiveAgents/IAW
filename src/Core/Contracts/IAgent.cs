using Core.UI;

namespace Core.Contracts;

public interface IAgent : IGrainWithStringKey
{
    static virtual string AgentDisplayName => "";
    static virtual string AgentDescription => "";
    static virtual string[] AgentCapabilities => [];
    static virtual string AgentInstructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
    static virtual string[] AgentRoutingExamples => [];

    // Conversation
    [ResponseTimeout("00:05:00")]
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct);
    [ResponseTimeout("00:05:00")]
    IAsyncEnumerable<string> GetResponseStream(ChatMessage message, CancellationToken ct);

    [ResponseTimeout("00:05:00")]
    Task<string> GetResponse(string prompt, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken ct);
    Task ClearHistory(CancellationToken ct);

    // Rich responses & callbacks
    Task<AgentResponse> GetRichResponse(string prompt, CancellationToken ct = default);
    Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct = default);

    // Scheduling
    Task ScheduleJob(string name, TimeSpan delay, string prompt, CancellationToken ct = default);
    Task ScheduleRecurringJob(string name, TimeSpan interval, string prompt, CancellationToken ct = default);
    Task CancelJob(string name, CancellationToken ct = default);
    Task<List<ScheduledJobInfo>> ListJobs(CancellationToken ct = default);

    // State
    Task<AgentState> GetState(CancellationToken ct);
    Task SetWorkspace(string path, CancellationToken ct);

    // Metadata
    Task<AgentMetadata> GetMetadata(CancellationToken ct);
    Task<AgentCapabilities> GetCapabilities(CancellationToken ct);

    // Events
    Task<List<AgentEvent>> GetEventLog(CancellationToken ct);

    // Streams
    Task<IReadOnlyList<string>> GetActiveSubscriptions(CancellationToken ct);

    // Usage
    Task<AgentUsage?> GetLastUsage(CancellationToken ct);

    // Lifecycle
    Task Cancel(CancellationToken ct);
}