using Core.Orchestration;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Integration.Tests;

public class OrchestrationIntegrationTests : AgentTest<CodeOrchestratorAgent>
{
    [Fact]
    public async Task CodeOrchestrator_creates_and_tracks_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = (ICodeOrchestrator)Agent("code-orchestrator");

        var taskId = await orchestrator.CreateTask("Analyze src/Core/Agent.cs", ct);
        Assert.NotNull(taskId);

        var state = await orchestrator.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Created, state.Status);
        Assert.Equal("Analyze src/Core/Agent.cs", state.Description);
    }

    [Fact]
    public async Task CodeOrchestrator_pause_resume_lifecycle()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = (ICodeOrchestrator)Agent("code-orchestrator-lifecycle");

        var taskId = await orchestrator.CreateTask("Multi-step refactor", ct);
        await orchestrator.PauseTask(taskId, ct);
        var paused = await orchestrator.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Paused, paused.Status);

        await orchestrator.ResumeTask(taskId, ct);
        var resumed = await orchestrator.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Running, resumed.Status);
    }

    [Fact]
    public async Task TaskSupervisor_registers_and_reports_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var supervisor = (ITaskSupervisor)Cluster.GrainFactory.GetGrain<ITaskSupervisor>("task-supervisor");

        await supervisor.RegisterTask("integration-1", "code-orchestrator", 5, ct);
        var health = await supervisor.GetTaskHealth("integration-1", ct);
        Assert.NotNull(health);
        Assert.Equal(0, health.CompletedSteps);
        Assert.Equal(5, health.StepCount);

        await supervisor.ReportProgress("integration-1", 3, ct);
        health = await supervisor.GetTaskHealth("integration-1", ct);
        Assert.Equal(3, health!.CompletedSteps);
        Assert.False(health.IsStalled);
    }
}
