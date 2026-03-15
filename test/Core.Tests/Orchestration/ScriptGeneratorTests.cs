using Core.Orchestration;
using IAW.Agents.CSharp;
using IAW.Agents.Infrastructure;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class ScriptGeneratorTests
{
    static ScriptGeneratorTests()
    {
        // force assembly loading so InterfaceCatalog.Discover() finds agent interfaces
        _ = typeof(IRoslyn).Assembly;
        _ = typeof(IFileSystem).Assembly;
    }
    [Fact]
    public void Generate_uses_typed_interfaces()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "analyze code" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("GetGrain<IRoslyn>", script);
        Assert.DoesNotContain("GetGrain<IAgent>", script);
    }

    [Fact]
    public void Generate_uses_GetResponse_not_SendMessageAsync()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("GetResponse", script);
        Assert.DoesNotContain("SendMessageAsync", script);
    }

    [Fact]
    public void Generate_uses_SetWorkspace_not_SetWorkspaceAsync()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "file-system", "read", new() { ["workspace"] = "/project", ["message"] = "read files" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("SetWorkspace", script);
        Assert.DoesNotContain("SetWorkspaceAsync", script);
    }

    [Fact]
    public void Generate_includes_agent_namespace_usings()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("using IAW.Agents.CSharp;", script);
    }

    [Fact]
    public void Generate_with_workspace_sets_workspace_on_all_agents()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000, "/workspace");
        Assert.Contains("SetWorkspace(\"/workspace\"", script);
    }

    [Fact]
    public void Generate_resolves_grain_id_from_catalog()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "file-system", "read", new() { ["message"] = "list files" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("GetGrain<IFileSystem>(\"file-system\")", script);
        Assert.DoesNotContain("orchestrated-", script);
    }

    [Fact]
    public void Generate_falls_back_to_IAgent_for_unknown_agent_type()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "unknown-agent", "do-something", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("GetGrain<IAgent>(\"unknown-agent\")", script);
    }

    [Fact]
    public void Generate_emits_progress_protocol_lines()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("[PROGRESS:1]", script);
    }

    [Fact]
    public void Generate_emits_error_protocol_on_failure()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("[ERROR:1]", script);
        Assert.Contains("ex.GetType().Name", script);
    }

    [Fact]
    public void Generate_emits_completed_protocol()
    {
        var plan = new OrchestrationPlan("test summary", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("[COMPLETED] test summary", script);
    }

    [Fact]
    public void Generate_critical_step_exits_on_failure()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" }, Critical: true)
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("return 1;", script);
    }

    [Fact]
    public void Generate_non_critical_step_continues_on_failure()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" }, Critical: false)
        ]);
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.DoesNotContain("return 1;", script);
    }

    [Fact]
    public void Generate_includes_taskId_comment()
    {
        var plan = new OrchestrationPlan("test", [
            new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
        ], TaskId: "task-abc");
        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        Assert.Contains("// TaskId: task-abc", script);
    }
}
