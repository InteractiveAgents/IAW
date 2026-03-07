using IAW.Core.Messages;

namespace IAW.Core.Communication;

public interface IAgentObserver<TEvent> : IGrainObserver where TEvent : INotification
{
    void OnEvent(TEvent evt);
    void OnError(Exception ex);
}
