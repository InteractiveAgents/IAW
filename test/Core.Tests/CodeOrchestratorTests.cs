using Core.Contracts;
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

    [Fact]
    public async Task ExecuteCodeOrchestration_CreatesWorkspaceFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var testWorkspace = Path.Combine(Path.GetTempPath(), $"iaw-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("IAW__Workspace", testWorkspace);

        try
        {
            var orchestrator = Cluster.GrainFactory.GetGrain<ICodeOrchestrator>("test-orch-" + Guid.NewGuid().ToString("N")[..6]);
            var result = await orchestrator.ExecuteCodeOrchestration(
                "INTENT: Test. STEPS: 1. Print hello", ct);

            Assert.NotNull(result);
            Assert.NotEmpty(result);

            var tasksDir = Path.Combine(testWorkspace, "tasks");
            Assert.True(Directory.Exists(tasksDir), $"Tasks dir should exist at {tasksDir}. Result was: {result[..Math.Min(500, result.Length)]}");

            var taskDirs = Directory.GetDirectories(tasksDir);
            Assert.Single(taskDirs);

            var taskDir = taskDirs[0];
            Assert.True(File.Exists(Path.Combine(taskDir, "plan.md")), "plan.md should exist");
            Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.cs")), "orchestration.cs should exist");
            Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.csproj")), "orchestration.csproj should exist");
            Assert.True(File.Exists(Path.Combine(taskDir, "log.txt")), "log.txt should exist");

            // MockChatClient returns "mock-response" which isn't valid C# — dotnet run fails
            // Result should contain workspace path
            Assert.Contains("Workspace:", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IAW__Workspace", null);
            if (Directory.Exists(testWorkspace))
                Directory.Delete(testWorkspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteCodeOrchestration_ReturnsErrorOnBadPath()
    {
        var ct = TestContext.Current.CancellationToken;
        Environment.SetEnvironmentVariable("IAW__Workspace", "Z:\\nonexistent\\path");

        try
        {
            var orchestrator = Cluster.GrainFactory.GetGrain<ICodeOrchestrator>("test-orch-err-" + Guid.NewGuid().ToString("N")[..6]);
            var result = await orchestrator.ExecuteCodeOrchestration("test plan", ct);

            Assert.Contains("CodeOrchestrator error:", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IAW__Workspace", null);
        }
    }
}
