using Core.Contracts;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ProjectTests : AgentTest<Project>
{
    [Fact]
    public async Task GetResponse_WorksLikeStandardAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project"));
        var response = await project.GetResponse("hello", ct);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetHistory_TracksConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project-hist"));
        await project.GetResponse("test message", ct);
        var history = await project.GetHistory(ct);
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public async Task GetMetadata_ReturnsProjectDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project-meta"));
        var metadata = await project.GetMetadata(ct);
        Assert.Equal("Project", metadata.DisplayName);
    }

    [Fact]
    public async Task ClearHistory_EmptiesHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project-clear"));
        await project.GetResponse("hello", ct);
        await project.ClearHistory(ct);
        var history = await project.GetHistory(ct);
        Assert.Empty(history);
    }

    [Fact]
    public async Task AddTask_CreatesTaskWithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-add-task"));

        var task = await project.AddTask("Build login page", TaskPriority.High, ct);

        Assert.NotNull(task);
        Assert.NotEmpty(task.Id);
        Assert.Equal(8, task.Id.Length);
        Assert.Equal("Build login page", task.Description);
        Assert.Equal(TaskPriority.High, task.Priority);
        Assert.Equal(ProjectTaskStatus.Pending, task.Status);
        Assert.True(task.CreatedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task GetTasks_ReturnsAddedTasks()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-get-tasks"));

        await project.AddTask("Task A", TaskPriority.Low, ct);
        await project.AddTask("Task B", TaskPriority.Critical, ct);

        var tasks = await project.GetTasks(ct);

        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, t => t.Description == "Task A" && t.Priority == TaskPriority.Low);
        Assert.Contains(tasks, t => t.Description == "Task B" && t.Priority == TaskPriority.Critical);
    }

    [Fact]
    public async Task UpdateTask_ChangesStatusToDone()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-update-done"));

        var task = await project.AddTask("Fix bug", TaskPriority.Medium, ct);
        await project.UpdateTask(task.Id, ProjectTaskStatus.Done, ct);

        var tasks = await project.GetTasks(ct);
        var updated = Assert.Single(tasks);

        Assert.Equal(ProjectTaskStatus.Done, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task UpdateTask_ChangesStatusToInProgress_NoCompletedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-update-ip"));

        var task = await project.AddTask("Refactor module", TaskPriority.High, ct);
        await project.UpdateTask(task.Id, ProjectTaskStatus.InProgress, ct);

        var tasks = await project.GetTasks(ct);
        var updated = Assert.Single(tasks);

        Assert.Equal(ProjectTaskStatus.InProgress, updated.Status);
        Assert.Null(updated.CompletedAt);
    }

    [Fact]
    public async Task UpdateTask_Cancelled_SetsCompletedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-update-cancel"));

        var task = await project.AddTask("Abandoned feature", TaskPriority.Low, ct);
        await project.UpdateTask(task.Id, ProjectTaskStatus.Cancelled, ct);

        var tasks = await project.GetTasks(ct);
        var updated = Assert.Single(tasks);

        Assert.Equal(ProjectTaskStatus.Cancelled, updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task UpdateTask_InvalidId_ThrowsKeyNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-update-bad"));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => project.UpdateTask("nonexistent", ProjectTaskStatus.Done, ct));
    }

    [Fact]
    public async Task GetTasks_EmptyByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-empty-tasks"));

        var tasks = await project.GetTasks(ct);

        Assert.Empty(tasks);
    }

    [Fact]
    public async Task ScheduleJob_CreatesJobWithCorrectFields()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-schedule-job"));

        var interval = TimeSpan.FromMinutes(30);
        var job = await project.ScheduleJob("Daily report", interval, "Generate a daily status report", ct);

        Assert.NotNull(job);
        Assert.NotEmpty(job.Id);
        Assert.Equal(8, job.Id.Length);
        Assert.Equal("Daily report", job.Name);
        Assert.Equal("Generate a daily status report", job.Description);
        Assert.Equal(interval, job.Interval);
        Assert.True(job.Active);
        Assert.True(job.NextRunAt > DateTimeOffset.UtcNow);
        Assert.Null(job.LastRunAt);
        Assert.Null(job.LastResult);
    }

    [Fact]
    public async Task CancelJob_DeactivatesJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-cancel-job"));

        var job = await project.ScheduleJob("Cleanup", TimeSpan.FromMinutes(60), "Clean temp files", ct);
        await project.CancelJob(job.Id, ct);

        var dashboard = await project.GetDashboard(ct);
        var cancelledJob = dashboard.Jobs.Single(j => j.Id == job.Id);
        Assert.False(cancelledJob.Active);
    }

    [Fact]
    public async Task CancelJob_InvalidId_ThrowsKeyNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = (IProject)Agent(UniqueId("project-cancel-bad"));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => project.CancelJob("nonexistent", ct));
    }
}
