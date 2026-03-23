using Core.Messages;

namespace Core.Communication;

public interface IReceiver<TMessage> where TMessage : IAgentMessage
{
    Task<MessageReceipt> ReceiveAsync(TMessage message, CancellationToken ct = default);
    Task<bool> CanReceiveAsync(CancellationToken ct = default);
}