using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class OrchestrationPlanTests
{
    [Fact]
    public void Plan_BackwardCompat_TwoArgConstructor()
    {
        var plan = new OrchestrationPlan("Test", []);
        Assert.Equal("", plan.TaskId);
        Assert.Equal("", plan.ProjectId);
        Assert.Null(plan.GlobalParameters);
    }

    [Fact]
    public void Plan_WithTaskAndProject()
    {
        var plan = new OrchestrationPlan("Test", [], TaskId: "task-123", ProjectId: "user/general");
        Assert.Equal("task-123", plan.TaskId);
        Assert.Equal("user/general", plan.ProjectId);
    }

    [Fact]
    public void PlanStep_DefaultCritical_IsTrue()
    {
        var step = new PlanStep(1, "IFileSystem", "ReadFileAsync", new Dictionary<string, string>());
        Assert.True(step.Critical);
    }

    [Fact]
    public void PlanStep_ExplicitNonCritical()
    {
        var step = new PlanStep(1, "IWebSearch", "SearchAsync", new Dictionary<string, string>(), Critical: false);
        Assert.False(step.Critical);
    }
}
