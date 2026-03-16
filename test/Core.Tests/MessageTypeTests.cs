using Core.Messages;
using Xunit;

namespace IAW.Core.Tests;

public class MessageTypeTests
{
    [Fact]
    public void AllMessageTypes_PreserveProperties()
    {
        var ts = DateTimeOffset.UtcNow;
        var cmd = new AssignTaskCommand("src", "corr", ts, "do something");
        Assert.Equal("src", cmd.SourceAgentId);
        Assert.Equal("corr", cmd.CorrelationId);
        Assert.Equal(ts, cmd.Timestamp);
        Assert.Equal("do something", cmd.Description);

        var evt = new CodeChangedEvent(["file.cs"], "test", "test change", "src2", "corr2", ts);
        Assert.Equal("src2", evt.SourceAgentId);
        Assert.Equal("corr2", evt.CorrelationId);
        Assert.Equal(ts, evt.Timestamp);
        Assert.Contains("file.cs", evt.ChangedFiles);
    }
}
