using IAW.Core.Messages;

namespace IAW.Core.Communication;

public interface IBroadcaster<TMessage> where TMessage : IAgentMessage
{
    Task<BroadcastResult> BroadcastAsync(TMessage message, CancellationToken ct = default);
    Task RegisterReceiverAsync(string receiverId);
    Task UnregisterReceiverAsync(string receiverId);
    Task<IReadOnlyList<string>> GetReceiversAsync();
}
