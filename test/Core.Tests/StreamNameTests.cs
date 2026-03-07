using IAW.Core;
using IAW.Core.Messages;
using Xunit;

namespace IAW.Core.Tests;

public class StreamNameTests
{
    [Theory]
    [MemberData(nameof(StreamNameCases))]
    public void EventTypeToStreamName_ReturnsExpectedName(Type eventType, string expectedStreamName)
    {
        var result = Agent.EventTypeToStreamName(eventType);
        Assert.Equal(expectedStreamName, result);
    }

    public static TheoryData<Type, string> StreamNameCases => new()
    {
        { typeof(CodeChangedEvent), "code.changed" },
        { typeof(BuildCompletedEvent), "build.completed" },
        { typeof(TestResultEvent), "test.result" },
        { typeof(DeployCompletedEvent), "deploy.completed" },
        { typeof(HealthCheckEvent), "health.check" },
        { typeof(AgentActivatedEvent), "agent.activated" },
        { typeof(StateChangedEvent), "state.changed" },
        { typeof(AssignTaskCommand), "assign.task" },
        { typeof(ProgressNotification), "progress" },
        { typeof(AlertNotification), "alert" },
        { typeof(ReviewRequestNotification), "review.request" },
    };
}
