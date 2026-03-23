using System.Text;

namespace Core.Contracts;

public static class DashboardRenderer
{
    public static string Render(ProjectDashboard dashboard)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\ud83d\udccb Project Dashboard");
        sb.AppendLine();

        var activeTasks = dashboard.Tasks
            .Where(t => t.Status is ProjectTaskStatus.Pending or ProjectTaskStatus.InProgress)
            .ToList();

        if (activeTasks.Count > 0)
        {
            sb.AppendLine($"\u25b8 Active ({activeTasks.Count})");
            foreach (var t in activeTasks.Take(10))
                sb.AppendLine($"  {t.Description} \u2014 {t.Status}");
            sb.AppendLine();
        }

        var doneCount = dashboard.Tasks.Count(t => t.Status == ProjectTaskStatus.Done);
        if (doneCount > 0) sb.AppendLine($"\u25b8 Done ({doneCount})");

        var activeJobs = dashboard.Jobs.Where(j => j.Active).ToList();
        if (activeJobs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"\u25b8 Scheduled ({activeJobs.Count})");
            foreach (var j in activeJobs)
                sb.AppendLine($"  {j.Name} \u2014 next: {j.NextRunAt:HH:mm}");
        }

        if (dashboard.Files.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"\u25b8 Files ({dashboard.Files.Count})");
            foreach (var f in dashboard.Files.Take(10))
                sb.AppendLine($"  {f.FileName}{(f.Ingested ? " (indexed)" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine($"Updated: {dashboard.GeneratedAt:yyyy-MM-dd HH:mm} UTC");

        return sb.ToString();
    }
}