using System.ComponentModel;
using System.Text;
using Core;
using Core.Contracts;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Core;

public abstract partial class Agent : IRemindable
{
    protected IDurableDictionary<string, ScheduledJobItem> ScheduledJobs => durableState.ScheduledJobs;

    public virtual async Task ScheduleJob(string name, TimeSpan delay, string prompt, CancellationToken ct = default)
    {
        var job = new ScheduledJobItem(name, prompt, delay, DateTimeOffset.UtcNow, IsRecurring: false, null, null);
        durableState.ScheduledJobs[name] = job;
        await WriteStateAsync(ct);
        await this.RegisterOrUpdateReminder(name, delay, delay);
    }

    public virtual async Task ScheduleRecurringJob(string name, TimeSpan interval, string prompt, CancellationToken ct = default)
    {
        var job = new ScheduledJobItem(name, prompt, interval, DateTimeOffset.UtcNow, IsRecurring: true, null, null);
        durableState.ScheduledJobs[name] = job;
        await WriteStateAsync(ct);
        await this.RegisterOrUpdateReminder(name, TimeSpan.Zero, interval);
    }

    public virtual async Task CancelJob(string name, CancellationToken ct = default)
    {
        durableState.ScheduledJobs.Remove(name);
        await WriteStateAsync(ct);
        var reminder = await this.GetReminder(name);
        if (reminder is not null)
            await this.UnregisterReminder(reminder);
    }

    public virtual Task<List<ScheduledJobInfo>> ListJobs(CancellationToken ct = default)
    {
        var jobs = new List<ScheduledJobInfo>();
        foreach (var kvp in durableState.ScheduledJobs)
        {
            var item = kvp.Value;
            var nextDue = item.LastRunAt.HasValue
                ? item.LastRunAt.Value + item.Interval
                : item.CreatedAt + item.Interval;
            jobs.Add(new ScheduledJobInfo(item.Name, item.Prompt, item.Interval, nextDue));
        }
        return Task.FromResult(jobs);
    }

    public virtual async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!durableState.ScheduledJobs.TryGetValue(reminderName, out var job))
            return;

        await OnScheduledJobDueAsync(job, AgentCancellation);

        if (!job.IsRecurring)
        {
            durableState.ScheduledJobs.Remove(reminderName);
            var reminder = await this.GetReminder(reminderName);
            if (reminder is not null)
                await this.UnregisterReminder(reminder);
        }

        await WriteStateAsync(AgentCancellation);
    }

    protected virtual async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        var chatHistory = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, job.Prompt)
        };
        var tools = DefineTools();
        var options = tools.Count > 0 ? new ChatOptions { Tools = [.. tools] } : null;
        string result;
        try
        {
            var response = await ChatClient.GetResponseAsync(chatHistory, options, ct);
            result = response.Text ?? "";
        }
        catch (Exception ex)
        {
            result = BuildSafeErrorMessage(ex);
        }

        var updated = job with { LastRunAt = DateTimeOffset.UtcNow, LastResult = result };
        durableState.ScheduledJobs[job.Name] = updated;

        await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, object>
        {
            ["JobName"] = job.Name,
            ["Result"] = result
        }, ct);
    }

    [Description("Schedule a job to run once after a delay")]
    private async Task<string> ScheduleJobCommand(
        [Description("What to do")] string description,
        [Description("Delay in minutes")] int delayMinutes)
    {
        var name = Guid.NewGuid().ToString("N")[..8];
        await ScheduleJob(name, TimeSpan.FromMinutes(delayMinutes), description, AgentCancellation);
        return $"Job '{name}' scheduled — runs in {delayMinutes} minutes";
    }

    [Description("Schedule a recurring job")]
    private async Task<string> ScheduleRecurringJobCommand(
        [Description("What to do each run")] string description,
        [Description("Interval in minutes between runs")] int intervalMinutes)
    {
        var name = Guid.NewGuid().ToString("N")[..8];
        await ScheduleRecurringJob(name, TimeSpan.FromMinutes(intervalMinutes), description, AgentCancellation);
        return $"Recurring job '{name}' scheduled — runs every {intervalMinutes} minutes";
    }

    [Description("Cancel a scheduled job by name")]
    private async Task<string> CancelJobCommand([Description("Job name to cancel")] string jobName)
    {
        if (!durableState.ScheduledJobs.ContainsKey(jobName))
            return $"Job '{jobName}' not found";
        await CancelJob(jobName, AgentCancellation);
        return $"Job '{jobName}' cancelled";
    }

    [Description("List all scheduled jobs")]
    private async Task<string> ListJobsCommand()
    {
        var jobs = await ListJobs(AgentCancellation);
        if (jobs.Count == 0) return "No scheduled jobs";
        var sb = new StringBuilder();
        foreach (var job in jobs)
        {
            var nextDue = job.NextDue?.ToString("g") ?? "unknown";
            sb.AppendLine($"- [{job.Name}] {job.Prompt} (every {job.Interval.TotalMinutes}min, next: {nextDue})");
        }
        return sb.ToString();
    }
}
