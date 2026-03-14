using Core.Context;
using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests.Context;

public class TaskContextProviderTests
{
    [Fact]
    public void Has_correct_name()
    {
        var provider = new TaskContextProvider(new List<ProjectTask>());
        Assert.Equal("task-context", provider.Name);
    }

    [Fact]
    public void Implements_IAgentContextProvider()
    {
        Assert.True(typeof(IAgentContextProvider).IsAssignableFrom(typeof(TaskContextProvider)));
    }

    [Fact]
    public async Task Returns_empty_on_error()
    {
        var provider = new TaskContextProvider(null!);
        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_empty_when_no_tasks()
    {
        var provider = new TaskContextProvider(new List<ProjectTask>());
        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Returns_active_task_entries()
    {
        var tasks = new List<ProjectTask>
        {
            new() { Id = "t1", Description = "Build feature", Priority = TaskPriority.High, Status = ProjectTaskStatus.InProgress },
            new() { Id = "t2", Description = "Write tests", Priority = TaskPriority.Medium, Status = ProjectTaskStatus.Pending }
        };
        var provider = new TaskContextProvider(tasks);

        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Contains("[active task]") && s.Contains("t1") && s.Contains("Build feature"));
        Assert.Contains(result, s => s.Contains("[active task]") && s.Contains("t2") && s.Contains("Write tests"));
    }

    [Fact]
    public async Task Returns_completed_count()
    {
        var tasks = new List<ProjectTask>
        {
            new() { Id = "t1", Description = "Done task", Status = ProjectTaskStatus.Done },
            new() { Id = "t2", Description = "Another done", Status = ProjectTaskStatus.Done },
            new() { Id = "t3", Description = "Active task", Status = ProjectTaskStatus.InProgress }
        };
        var provider = new TaskContextProvider(tasks);

        var result = await provider.GetContextAsync("test-agent", "query", TestContext.Current.CancellationToken);

        Assert.Contains(result, s => s.Contains("[completed] 2 tasks completed"));
        Assert.Contains(result, s => s.Contains("[active task]") && s.Contains("t3"));
    }
}
