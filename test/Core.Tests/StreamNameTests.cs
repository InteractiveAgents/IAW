using Core.Messages;
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
        { typeof(AssignTaskCommand), "assign.task" },
        { typeof(IAW.Agents.Messages.BuildMetricsCollectedEvent), "build.metrics.collected" },
        { typeof(IAW.Agents.Messages.SpecReadyEvent), "spec.ready" },
        { typeof(IAW.Agents.Messages.TestsPassedEvent), "tests.passed" },
    };
}
