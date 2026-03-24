using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests;

public class DashboardRenderTests
{
    [Fact]
    public void Render_EmptyDashboard_ContainsHeaderAndTimestamp()
    {
        var dashboard = new ProjectDashboard { GeneratedAt = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero) };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Project Dashboard", result);
        Assert.Contains("Updated: 2026-03-14 12:00 UTC", result);
        Assert.DoesNotContain("Active", result);
        Assert.DoesNotContain("Done", result);
        Assert.DoesNotContain("Scheduled", result);
        Assert.DoesNotContain("Files", result);
    }

    [Fact]
    public void Render_WithActiveTasks_ShowsActiveSection()
    {
        var dashboard = new ProjectDashboard
        {
            Tasks =
            [
                new ProjectTask { Id = "t1", Description = "Build login page", Status = ProjectTaskStatus.Pending, Priority = TaskPriority.High },
                new ProjectTask { Id = "t2", Description = "Fix bug", Status = ProjectTaskStatus.InProgress, Priority = TaskPriority.Critical }
            ],
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Active (2)", result);
        Assert.Contains("Build login page", result);
        Assert.Contains("Fix bug", result);
    }

    [Fact]
    public void Render_WithDoneTasks_ShowsDoneCount()
    {
        var dashboard = new ProjectDashboard
        {
            Tasks =
            [
                new ProjectTask { Id = "t1", Description = "Completed task", Status = ProjectTaskStatus.Done, Priority = TaskPriority.Low }
            ],
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Done (1)", result);
        Assert.DoesNotContain("Completed task", result); // done tasks only show count
    }

    [Fact]
    public void Render_WithActiveJobs_ShowsScheduledSection()
    {
        var nextRun = new DateTimeOffset(2026, 3, 14, 15, 30, 0, TimeSpan.Zero);
        var dashboard = new ProjectDashboard
        {
            Jobs =
            [
                new ScheduledJob { Id = "j1", Name = "Daily sync", Active = true, NextRunAt = nextRun },
                new ScheduledJob { Id = "j2", Name = "Inactive job", Active = false }
            ],
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Scheduled (1)", result);
        Assert.Contains("Daily sync", result);
        Assert.Contains("15:30", result);
        Assert.DoesNotContain("Inactive job", result);
    }

    [Fact]
    public void Render_WithFiles_ShowsFilesSection()
    {
        var dashboard = new ProjectDashboard
        {
            Files =
            [
                new FileReference("blob://test", "report.pdf", "application/pdf", 1024, true, DateTimeOffset.UtcNow),
                new FileReference("blob://test2", "notes.txt", "text/plain", 512, false, DateTimeOffset.UtcNow)
            ],
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Files (2)", result);
        Assert.Contains("report.pdf (indexed)", result);
        Assert.Contains("notes.txt", result);
        Assert.DoesNotContain("notes.txt (indexed)", result);
    }

    [Fact]
    public void Render_ActiveTasksCappedAt10()
    {
        var tasks = Enumerable.Range(1, 15)
            .Select(i => new ProjectTask { Id = $"t{i}", Description = $"Task {i}", Status = ProjectTaskStatus.Pending, Priority = TaskPriority.Medium })
            .ToList();

        var dashboard = new ProjectDashboard { Tasks = tasks, GeneratedAt = DateTimeOffset.UtcNow };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Active (15)", result);
        Assert.Contains("Task 10", result);
        Assert.DoesNotContain("Task 11", result);
    }

    [Fact]
    public void Render_FilesCappedAt10()
    {
        var files = Enumerable.Range(1, 15)
            .Select(i => new FileReference($"blob://f{i}", $"file{i}.txt", "text/plain", 100, false, DateTimeOffset.UtcNow))
            .ToList();

        var dashboard = new ProjectDashboard { Files = files, GeneratedAt = DateTimeOffset.UtcNow };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Files (15)", result);
        Assert.Contains("file10.txt", result);
        Assert.DoesNotContain("file11.txt", result);
    }

    [Fact]
    public void Render_MixedTaskStatuses_SeparatesActiveAndDone()
    {
        var dashboard = new ProjectDashboard
        {
            Tasks =
            [
                new ProjectTask { Id = "t1", Description = "Active task", Status = ProjectTaskStatus.InProgress, Priority = TaskPriority.High },
                new ProjectTask { Id = "t2", Description = "Done task", Status = ProjectTaskStatus.Done, Priority = TaskPriority.Low },
                new ProjectTask { Id = "t3", Description = "Cancelled task", Status = ProjectTaskStatus.Cancelled, Priority = TaskPriority.Medium }
            ],
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var result = DashboardRenderer.Render(dashboard);

        Assert.Contains("Active (1)", result);
        Assert.Contains("Active task", result);
        Assert.Contains("Done (1)", result);
        Assert.DoesNotContain("Cancelled task", result); // cancelled tasks are not shown in active or done
    }

    [Fact]
    public void Render_NoMarkdownV2SpecialChars()
    {
        var dashboard = new ProjectDashboard
        {
            Tasks =
            [
                new ProjectTask { Id = "t1", Description = "Simple task", Status = ProjectTaskStatus.Pending, Priority = TaskPriority.Low }
            ],
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var result = DashboardRenderer.Render(dashboard);

        // plain text format — no MarkdownV2 escape characters
        Assert.DoesNotContain("\\*", result);
        Assert.DoesNotContain("\\_", result);
        Assert.DoesNotContain("\\[", result);
    }
}