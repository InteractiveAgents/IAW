using Core.Messages;
using Core.Messages.Events;
using System.Reflection;
using Xunit;

namespace IAW.Core.Tests.Communication;

public class EventTypeTests
{
    [Fact]
    public void StepProgressEvent_implements_ITaskStreamEvent_and_IEvent()
    {
        ITaskStreamEvent evt = new StepProgressEvent("agent-1", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow,
            "task-1", "analyzing", null);
        Assert.Equal("task-1", evt.TaskId);
        Assert.IsAssignableFrom<IEvent>(evt);
        Assert.IsAssignableFrom<IAgentMessage>(evt);
    }

    [Fact]
    public void All_task_stream_events_implement_IEvent_and_IAgentMessage()
    {
        var types = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ITaskStreamEvent)) && !t.IsInterface);
        Assert.All(types, t =>
        {
            Assert.True(t.IsAssignableTo(typeof(IEvent)));
            Assert.True(t.IsAssignableTo(typeof(IAgentMessage)));
        });
    }

    [Fact]
    public void All_task_stream_events_have_GenerateSerializer()
    {
        var types = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ITaskStreamEvent)) && !t.IsInterface);
        Assert.All(types, t =>
            Assert.NotNull(t.GetCustomAttribute<GenerateSerializerAttribute>()));
    }
}