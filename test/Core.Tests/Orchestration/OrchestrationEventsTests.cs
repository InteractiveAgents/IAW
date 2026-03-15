using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class OrchestrationEventsTests
{
    [Fact]
    public void ProgressEvent_SetsTaskIdAsSourceAgent()
    {
        var evt = new OrchestrationProgressEvent("task-1", 0, "Working...", DateTimeOffset.UtcNow);
        Assert.Equal("task-1", evt.SourceAgentId);
        Assert.Equal("task-1", evt.CorrelationId);
    }

    [Fact]
    public void ErrorEvent_CapturesErrorDetails()
    {
        var evt = new OrchestrationErrorEvent("task-1", 2, "TimeoutException", "Connection timed out", DateTimeOffset.UtcNow);
        Assert.Equal(2, evt.StepIndex);
        Assert.Equal("TimeoutException", evt.ErrorType);
    }

    [Fact]
    public void ArtifactEvent_StoresBlobPath()
    {
        var evt = new OrchestrationArtifactEvent("task-1", "orchestration/task-1/report.xlsx", "report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        Assert.Equal("orchestration/task-1/report.xlsx", evt.BlobPath);
    }

    [Fact]
    public void CompletedEvent_ContainsArtifactPaths()
    {
        var evt = new OrchestrationCompletedEvent("task-1", "Done", ["path1", "path2"], DateTimeOffset.UtcNow);
        Assert.Equal(2, evt.ArtifactPaths.Count);
    }
}
