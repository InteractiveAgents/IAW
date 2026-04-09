using Core.Contracts;
using Core.Contracts.Events;
using IAW.Agents.Quality;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ValidatorAgentTests : AgentTest<ValidatorAgent>
{
    [Fact]
    public async Task ValidateConsistency_PassesWhenValuesFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("consist-pass");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Finance", AgentEventType.StepCompleted, "total: EUR 4231.50", "8 categories", DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "Excel", AgentEventType.StepCompleted, "created budget with EUR 4231.50 total", null, DateTimeOffset.UtcNow), ct);

        var validator = (IValidator)Agent(UniqueId("val"));
        var report = await validator.ValidateConsistencyAsync(taskId,
            new Dictionary<string, string> { ["total"] = "4231.50" }, ct);

        Assert.True(report.Passed);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task ValidateConsistency_FailsWhenValueMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("consist-fail");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Finance", AgentEventType.StepCompleted, "total: EUR 4231.50", null, DateTimeOffset.UtcNow), ct);

        var validator = (IValidator)Agent(UniqueId("val2"));
        var report = await validator.ValidateConsistencyAsync(taskId,
            new Dictionary<string, string> { ["total"] = "9999.99" }, ct);

        Assert.False(report.Passed);
        Assert.Single(report.Issues);
        Assert.Equal("warning", report.Issues[0].Severity);
    }

    [Fact]
    public async Task ValidateTask_ReportsEmptyLedger()
    {
        var ct = TestContext.Current.CancellationToken;
        var validator = (IValidator)Agent(UniqueId("val3"));
        var report = await validator.ValidateTaskAsync(
            UniqueId("empty-task"), "create a calculator app", ct);

        Assert.False(report.Passed);
        Assert.Single(report.Issues);
        Assert.Equal("critical", report.Issues[0].Severity);
        Assert.Contains("No events", report.Issues[0].Description);
    }

    [Fact]
    public async Task ValidateTask_WithEvents_CallsLLM()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("val-llm");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted, "analyzed project structure", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "build passed, 0 warnings", null, DateTimeOffset.UtcNow), ct);

        var validator = (IValidator)Agent(UniqueId("val4"));
        var report = await validator.ValidateTaskAsync(taskId, "build the project", ct);

        Assert.NotNull(report);
        Assert.Equal(taskId, report.TaskId);
    }
}
