using Core.Contracts.Notifications;
using Xunit;

namespace Core.Tests;

public class BroadcastNotificationTests
{
    [Fact]
    public void UINotification_TaskCompleted_HasRequiredFields()
    {
        var notif = UINotification.TaskCompleted(
            taskId: "finance-march",
            summary: "Budget created. Overspending: entertainment +45%",
            filePath: "budget_march.xlsx");

        Assert.Equal("task.completed", notif.Type);
        Assert.Equal("finance-march", notif.TaskId);
        Assert.Contains("Budget created", notif.Summary);
        Assert.Equal("budget_march.xlsx", notif.FilePath);
    }

    [Fact]
    public void UINotification_Progress_HasRequiredFields()
    {
        var notif = UINotification.Progress(
            taskId: "scaffold-app",
            message: "Step 3/5: Building project...",
            percentComplete: 60);

        Assert.Equal("progress", notif.Type);
        Assert.Equal(60, notif.PercentComplete);
    }

    [Fact]
    public void UINotification_Alert_HasSeverity()
    {
        var notif = UINotification.Alert(
            severity: "critical",
            message: "API latency spike: 2340ms");

        Assert.Equal("alert", notif.Type);
        Assert.Equal("critical", notif.Severity);
    }

    [Fact]
    public void UINotification_ApprovalNeeded_HasOptions()
    {
        var notif = UINotification.ApprovalNeeded(
            approvalId: "deploy-123",
            question: "Apply fix to ReportsController?",
            options: ["Yes", "No", "Show Diff"]);

        Assert.Equal("approval", notif.Type);
        Assert.Equal(3, notif.Options!.Count);
    }
}
