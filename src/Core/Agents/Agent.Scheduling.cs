using System.ComponentModel;
using System.Text;
using Core;
using Core.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.DurableJobs;
using Orleans.Journaling;

namespace IAW.Core;

public abstract partial class Agent : IDurableJobHandler
{
    protected IDurableDictionary<string, ScheduledJobItem> ScheduledJobs => durableState.ScheduledJobs;

    private ILocalDurableJobManager JobManager
        => ServiceProvider.GetRequiredService<ILocalDurableJobManager>();

    public virtual async Task ScheduleJob(string name, TimeSpan delay, string prompt, CancellationToken ct = default)
    {
        var dueTime = DateTimeOffset.UtcNow + delay;
        var metadata = BuildJobMetadata(prompt, isRecurring: false, interval: delay);
        var durableJob = await JobManager.ScheduleJobAsync(
            this.GetGrainId(), name, dueTime, metadata, ct);

        var job = new ScheduledJobItem(name, prompt, delay, DateTimeOffset.UtcNow,
            IsRecurring: false, null, null, durableJob.Id, durableJob.ShardId);
        durableState.ScheduledJobs[name] = job;
        await WriteStateAsync(ct);
    }

    public virtual async Task ScheduleRecurringJob(string name, TimeSpan interval, string prompt, CancellationToken ct = default)
    {
        var dueTime = DateTimeOffset.UtcNow + interval;
        var metadata = BuildJobMetadata(prompt, isRecurring: true, interval);
        var durableJob = await JobManager.ScheduleJobAsync(
            this.GetGrainId(), name, dueTime, metadata, ct);

        var job = new ScheduledJobItem(name, prompt, interval, DateTimeOffset.UtcNow,
            IsRecurring: true, null, null, durableJob.Id, durableJob.ShardId);
        durableState.ScheduledJobs[name] = job;
        await WriteStateAsync(ct);
    }

    public virtual async Task CancelJob(string name, CancellationToken ct = default)
    {
        if (durableState.ScheduledJobs.TryGetValue(name, out var jobItem)
            && jobItem.DurableJobId is not null
            && jobItem.DurableJobShardId is not null)
        {
            var durableJob = new DurableJob
            {
                Id = jobItem.DurableJobId,
                Name = name,
                ShardId = jobItem.DurableJobShardId,
                TargetGrainId = this.GetGrainId()
            };
            await JobManager.TryCancelDurableJobAsync(durableJob, ct);
        }

        durableState.ScheduledJobs.Remove(name);
        await WriteStateAsync(ct);
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

    public virtual async Task ExecuteJobAsync(IDurableJobContext context, CancellationToken cancellationToken)
    {
        var jobName = context.Job.Name;
        if (!durableState.ScheduledJobs.TryGetValue(jobName, out var job))
            return;

        await OnScheduledJobDueAsync(job, cancellationToken);

        if (job.IsRecurring)
        {
            var nextDueTime = DateTimeOffset.UtcNow + job.Interval;
            var metadata = BuildJobMetadata(job.Prompt, isRecurring: true, job.Interval);
            var newDurableJob = await JobManager.ScheduleJobAsync(
                this.GetGrainId(), jobName, nextDueTime, metadata, cancellationToken);

            var updated = durableState.ScheduledJobs[jobName] with
            {
                DurableJobId = newDurableJob.Id,
                DurableJobShardId = newDurableJob.ShardId
            };
            durableState.ScheduledJobs[jobName] = updated;
        }
        else
        {
            durableState.ScheduledJobs.Remove(jobName);
        }

        await WriteStateAsync(cancellationToken);
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

        await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, string>
        {
            [IAWConstants.PayloadKeys.ProjectKey] = this.GetPrimaryKeyString(),
            [IAWConstants.PayloadKeys.JobName] = job.Name,
            [IAWConstants.PayloadKeys.Result] = result
        }, ct);
    }

    private async Task RescheduleExistingJobsAsync(CancellationToken ct)
    {
        foreach (var kvp in durableState.ScheduledJobs)
        {
            var job = kvp.Value;
            var nextDue = job.LastRunAt.HasValue
                ? job.LastRunAt.Value + job.Interval
                : job.CreatedAt + job.Interval;

            if (nextDue <= DateTimeOffset.UtcNow)
                nextDue = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1);

            var metadata = BuildJobMetadata(job.Prompt, job.IsRecurring, job.Interval);
            var durableJob = await JobManager.ScheduleJobAsync(
                this.GetGrainId(), job.Name, nextDue, metadata, ct);

            durableState.ScheduledJobs[job.Name] = job with
            {
                DurableJobId = durableJob.Id,
                DurableJobShardId = durableJob.ShardId
            };
        }

        if (durableState.ScheduledJobs.Count > 0)
            await WriteStateAsync(ct);
    }

    private static IReadOnlyDictionary<string, string> BuildJobMetadata(string prompt, bool isRecurring, TimeSpan interval)
        => new Dictionary<string, string>
        {
            ["prompt"] = prompt,
            ["isRecurring"] = isRecurring.ToString(),
            ["intervalTicks"] = interval.Ticks.ToString()
        };

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
