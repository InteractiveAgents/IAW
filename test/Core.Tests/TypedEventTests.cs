using Core.Contracts;
using Core.Contracts.Events;
using Xunit;

namespace Core.Tests;

public class TypedEventTests
{
    [Fact]
    public void TaskEvent_HasRequiredFields()
    {
        var evt = new TaskEvent(
            Agent: "DotNet",
            Action: AgentEventType.BuildSucceeded,
            Result: "0 warnings",
            Detail: "net11.0 Release",
            Timestamp: DateTimeOffset.UtcNow);

        Assert.Equal("DotNet", evt.Agent);
        Assert.Equal(AgentEventType.BuildSucceeded, evt.Action);
        Assert.Equal("0 warnings", evt.Result);
    }

    [Fact]
    public void TaskEvent_TextRepresentation_IsCompact()
    {
        var evt = new TaskEvent(
            Agent: "FileSystem",
            Action: AgentEventType.FileCreated,
            Result: "budget.xlsx created",
            Detail: null,
            Timestamp: DateTimeOffset.UtcNow);

        var text = evt.ToContextLine();
        Assert.Contains("FileSystem", text);
        Assert.Contains("budget.xlsx created", text);
        Assert.True(text.Length < 120, $"Context line too long: {text.Length} chars");
    }

    [Fact]
    public void AgentEventType_CoversCoreDomains()
    {
        Assert.Equal("build.succeeded", AgentEventType.BuildSucceeded);
        Assert.Equal("build.failed", AgentEventType.BuildFailed);
        Assert.Equal("file.created", AgentEventType.FileCreated);
        Assert.Equal("file.read", AgentEventType.FileRead);
        Assert.Equal("test.passed", AgentEventType.TestPassed);
        Assert.Equal("test.failed", AgentEventType.TestFailed);
        Assert.Equal("commit.created", AgentEventType.CommitCreated);
        Assert.Equal("validation.passed", AgentEventType.ValidationPassed);
        Assert.Equal("validation.failed", AgentEventType.ValidationFailed);
        Assert.Equal("task.created", AgentEventType.TaskCreated);
        Assert.Equal("task.completed", AgentEventType.TaskCompleted);
        Assert.Equal("step.completed", AgentEventType.StepCompleted);
    }
}
