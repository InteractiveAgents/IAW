using IAW.Core.Messages;
using Xunit;

namespace IAW.Core.Tests;

public class MessageTypeTests
{
    [Fact]
    public void AgentActivatedEvent_ImplementsIEventAndIAgentMessage()
    {
        var evt = new AgentActivatedEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, "TestAgent");
        Assert.IsType<IEvent>(evt, exactMatch: false);
        Assert.IsType<IAgentMessage>(evt, exactMatch: false);
    }

    [Fact]
    public void AssignTaskCommand_ImplementsICommandAndIAgentMessage()
    {
        var cmd = new AssignTaskCommand("agent-1", "corr-1", DateTimeOffset.UtcNow, "Do something");
        Assert.IsAssignableFrom<ICommand>(cmd);
        Assert.IsAssignableFrom<IAgentMessage>(cmd);
    }

    [Fact]
    public void AlertNotification_ImplementsINotificationAndIAgentMessage()
    {
        var notif = new AlertNotification("agent-1", "corr-1", DateTimeOffset.UtcNow, "High", "Alert!");
        Assert.IsAssignableFrom<INotification>(notif);
        Assert.IsAssignableFrom<IAgentMessage>(notif);
    }

    [Fact]
    public void ProgressNotification_ImplementsINotification()
    {
        var notif = new ProgressNotification("agent-1", "corr-1", DateTimeOffset.UtcNow, "step-1", "running");
        Assert.IsAssignableFrom<INotification>(notif);
    }

    [Fact]
    public void CodeChangedEvent_ImplementsIEvent()
    {
        var evt = new CodeChangedEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, ["file.cs"]);
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void BuildCompletedEvent_ImplementsIEvent()
    {
        var evt = new BuildCompletedEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, true);
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void TestResultEvent_ImplementsIEvent()
    {
        var evt = new TestResultEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, true, 10, 0);
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void HealthCheckEvent_ImplementsIEvent()
    {
        var evt = new HealthCheckEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, "api", true);
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void DeployCompletedEvent_ImplementsIEvent()
    {
        var evt = new DeployCompletedEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, true, "prod");
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void ReviewRequestNotification_ImplementsINotification()
    {
        var notif = new ReviewRequestNotification("agent-1", "corr-1", DateTimeOffset.UtcNow, "file.cs", "Review this");
        Assert.IsAssignableFrom<INotification>(notif);
    }

    [Fact]
    public void StateChangedEvent_ImplementsIEvent()
    {
        var evt = new StateChangedEvent("agent-1", "corr-1", DateTimeOffset.UtcNow, "key", "old", "new");
        Assert.IsAssignableFrom<IEvent>(evt);
    }

    [Fact]
    public void AllMessageTypes_PreserveProperties()
    {
        var ts = DateTimeOffset.UtcNow;
        var alert = new AlertNotification("src", "corr", ts, "Critical", "msg");
        Assert.Equal("src", alert.SourceAgentId);
        Assert.Equal("corr", alert.CorrelationId);
        Assert.Equal(ts, alert.Timestamp);
        Assert.Equal("Critical", alert.Severity);
        Assert.Equal("msg", alert.Message);
    }
}
