using Core.Contracts;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class CodeOrchestratorTests : AgentTest<CodeOrchestratorAgent>
{
    [Fact]
    public async Task CodeOrchestrator_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Code Orchestrator", meta.DisplayName);
    }

    [Fact(Skip = "Integration test — requires real dotnet build + NuGet restore, too slow for CI with MockChatClient")]
    public async Task ExecuteCodeOrchestration_CreatesWorkspaceFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var testWorkspace = Path.Combine(Path.GetTempPath(), $"iaw-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("IAW__Workspace", testWorkspace);

        try
        {
            var orchestrator = (ICodeOrchestrator)Agent(UniqueId("orch"));
            var result = await orchestrator.ExecuteCodeOrchestration(
                "INTENT: Test. STEPS: 1. Print hello", new List<string> { "IShell" }, "", ct);

            Assert.NotNull(result);
            Assert.NotNull(result.WorkspacePath);
            Assert.NotEmpty(result.WorkspacePath);

            var tasksDir = Path.Combine(testWorkspace, "tasks");
            Assert.True(Directory.Exists(tasksDir), $"Tasks dir should exist at {tasksDir}. Summary: {result.Summary[..Math.Min(500, result.Summary.Length)]}");

            var taskDirs = Directory.GetDirectories(tasksDir);
            Assert.Single(taskDirs);

            var taskDir = taskDirs[0];
            Assert.True(File.Exists(Path.Combine(taskDir, "plan.md")), "plan.md should exist");
            Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.cs")), "orchestration.cs should exist");
            Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.csproj")), "orchestration.csproj should exist");
            Assert.True(File.Exists(Path.Combine(taskDir, "log.txt")), "log.txt should exist");

            // MockChatClient returns "mock-response" which isn't valid C# — build/run fails
            Assert.NotNull(result.TaskId);
            Assert.Contains(testWorkspace, result.WorkspacePath);
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
            var orchestrator = (ICodeOrchestrator)Agent(UniqueId("orch-err"));
            var result = await orchestrator.ExecuteCodeOrchestration("test plan", new List<string> { "IShell" }, "", ct);

            Assert.False(result.Success);
            Assert.Contains("error", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IAW__Workspace", null);
        }
    }
}