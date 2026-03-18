namespace Core.Messages;

public interface ITaskStreamEvent : IEvent
{
    string TaskId { get; }
}
