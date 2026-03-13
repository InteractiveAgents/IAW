using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class OrchestrationTypesTests
{
    [Fact]
    public void StepRecord_roundtrips_serialization()
    {
        var record = new StepRecord(0, "roslyn", "analyze", StepStatus.Pending, new() { ["path"] = "src/" });
        Assert.Equal("roslyn", record.AgentId);
        Assert.Equal(StepStatus.Pending, record.Status);
    }

    [Fact]
    public void StepResult_stores_duration_and_output()
    {
        var result = new StepResult("Build succeeded", TimeSpan.FromSeconds(12), "dot-net", DateTimeOffset.UtcNow);
        Assert.Equal("Build succeeded", result.Output);
    }

    [Fact]
    public void OrchestrationStatus_has_all_required_values()
    {
        Assert.True(Enum.IsDefined(typeof(OrchestrationStatus), OrchestrationStatus.Created));
        Assert.True(Enum.IsDefined(typeof(OrchestrationStatus), OrchestrationStatus.Running));
        Assert.True(Enum.IsDefined(typeof(OrchestrationStatus), OrchestrationStatus.Paused));
        Assert.True(Enum.IsDefined(typeof(OrchestrationStatus), OrchestrationStatus.Completed));
        Assert.True(Enum.IsDefined(typeof(OrchestrationStatus), OrchestrationStatus.Failed));
        Assert.True(Enum.IsDefined(typeof(OrchestrationStatus), OrchestrationStatus.Recovering));
    }
}
