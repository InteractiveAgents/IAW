using Core.Orchestration;
using IAW.Agents.CSharp;
using IAW.Agents.Infrastructure;
using Xunit;

namespace Core.Tests.Orchestration;

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
}
