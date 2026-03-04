namespace Core;

public interface IAgentMetadataBehavior
{
    Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default);
}

public interface IAgentStateBehavior
{
    Task SetStateAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetStateValueAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default);
    Task<int> IncrementAsync(string counterKey, CancellationToken ct = default);
}

public interface IAgentHistoryBehavior
{
    Task AddHistoryAsync(string role, string content, CancellationToken ct = default);
    Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default);
}

public interface IAgentEventsBehavior
{
    Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default);
    Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default);
}

// V1 convenience overloads not covered by IAgentV2
public interface IAgentNotificationsBehavior
{
    Task NotifyAsync(string topic, string payload, CancellationToken ct = default);
    Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default);
    Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default);
}

public interface IAgentTrackingBehavior
{
    Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default);
    Task StopTrackingAsync(CancellationToken ct = default);
    Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default);
}

// InvokeToolAsync is inherited from IAgentV2
public interface IAgentToolsBehavior;

// PublishStreamAsync is inherited from IAgentV2
public interface IAgentStreamsBehavior;
