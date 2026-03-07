using Core.V3.Messages;

namespace Core.V3.Communication;

public interface IAgentObserver<TEvent> : IGrainObserver where TEvent : INotification
{
    void OnEvent(TEvent evt);
    void OnError(Exception ex);
}
