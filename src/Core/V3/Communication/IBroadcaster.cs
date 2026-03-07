using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IBroadcaster<TMessage> where TMessage : IAgentMessage
{
    Task<BroadcastResult> BroadcastAsync(TMessage message, CancellationToken ct = default);
    Task RegisterReceiverAsync(string receiverId);
    Task UnregisterReceiverAsync(string receiverId);
    Task<IReadOnlyList<string>> GetReceiversAsync();
}
