namespace Core.V2;

public interface IAgentV2 : IGrainWithStringKey
{
    Task<AgentProfile> GetProfileAsync(CancellationToken ct = default);

    Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default);

    Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query = null, CancellationToken ct = default);

    Task SetMemoryAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken ct = default);

    Task AppendEventAsync(AgentEvent agentEvent, CancellationToken ct = default);
    Task<List<AgentEvent>> QueryEventsAsync(AgentEventQuery? query = null, CancellationToken ct = default);

    Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
    Task NotifyAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task ReceiveNotificationAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default);

    Task StartScheduleAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default);
    Task StopScheduleAsync(CancellationToken ct = default);
    Task<ScheduleStatus> GetScheduleStatusAsync(CancellationToken ct = default);

    Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);

    Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
}
