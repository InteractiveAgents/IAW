using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class TaskSupervisorTests : AgentTest<TaskSupervisorAgent>
{
    [Fact]
    public async Task Supervisor_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("task-supervisor");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Task Supervisor", meta.DisplayName);
    }

    [Fact]
    public async Task RegisterTask_and_GetTaskHealth_roundtrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var supervisor = Agent("task-supervisor");
        var typed = (ITaskSupervisor)supervisor;
        await typed.RegisterTask("test-task-1", "orchestrator-1", 5, ct);
        var health = await typed.GetTaskHealth("test-task-1", ct);
        Assert.NotNull(health);
        Assert.Equal("test-task-1", health!.TaskId);
        Assert.Equal(5, health.StepCount);
        Assert.Equal(0, health.CompletedSteps);
        Assert.False(health.IsStalled);
    }

    [Fact]
    public async Task ReportProgress_updates_completed_steps()
    {
        var ct = TestContext.Current.CancellationToken;
        var supervisor = Agent("task-supervisor");
        var typed = (ITaskSupervisor)supervisor;
        await typed.RegisterTask("test-task-2", "orchestrator-1", 3, ct);
        await typed.ReportProgress("test-task-2", 2, ct);
        var health = await typed.GetTaskHealth("test-task-2", ct);
        Assert.NotNull(health);
        Assert.Equal(2, health!.CompletedSteps);
    }

    [Fact]
    public async Task GetAllActiveTaskHealth_returns_all_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        var supervisor = Agent("task-supervisor");
        var typed = (ITaskSupervisor)supervisor;
        await typed.RegisterTask("task-a", "orch-1", 2, ct);
        await typed.RegisterTask("task-b", "orch-1", 4, ct);
        var all = await typed.GetAllActiveTaskHealth(ct);
        Assert.Equal(2, all.Count);
    }
}
