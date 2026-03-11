using Core.Orchestration;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class CodeOrchestratorTests : AgentTest<CodeOrchestratorAgent>
{
    [Fact]
    public async Task CreateTask_returns_task_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var taskId = await ((ICodeOrchestrator)agent).CreateTask("Fix build errors", ct);
        Assert.NotNull(taskId);
        Assert.StartsWith("task-", taskId);
    }

    [Fact]
    public async Task GetTaskState_returns_created_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var orch = (ICodeOrchestrator)agent;
        var taskId = await orch.CreateTask("Test task", ct);
        var state = await orch.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Created, state.Status);
        Assert.Equal("Test task", state.Description);
    }

    [Fact]
    public async Task PauseTask_updates_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var orch = (ICodeOrchestrator)agent;
        var taskId = await orch.CreateTask("Pausable task", ct);
        await orch.PauseTask(taskId, ct);
        var state = await orch.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Paused, state.Status);
    }

    [Fact]
    public async Task ResumeTask_after_pause_sets_running()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var orch = (ICodeOrchestrator)agent;
        var taskId = await orch.CreateTask("Resumable task", ct);
        await orch.PauseTask(taskId, ct);
        await orch.ResumeTask(taskId, ct);
        var state = await orch.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Running, state.Status);
    }

    [Fact]
    public async Task CodeOrchestrator_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Code Orchestrator", meta.DisplayName);
    }
}
