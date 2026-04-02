using Core.Contracts;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class TeamLeadDigestTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task StartDigest_SchedulesRecurringJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = (IThread)Agent(UniqueId("digest-schedule"));

        await thread.StartTaskDigestAsync("task-123", TimeSpan.FromMinutes(5), ct);

        var jobs = await ((IAgent)thread).ListJobs(ct);
        Assert.Contains(jobs, j => j.Name.Contains("task-123"));
    }

    [Fact]
    public async Task StopDigest_CancelsJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = (IThread)Agent(UniqueId("digest-stop"));

        await thread.StartTaskDigestAsync("task-456", TimeSpan.FromMinutes(5), ct);
        await thread.StopTaskDigestAsync("task-456", ct);

        var jobs = await ((IAgent)thread).ListJobs(ct);
        Assert.DoesNotContain(jobs, j => j.Name.Contains("task-456"));
    }

    [Fact]
    public async Task StartDigest_Idempotent_DoesNotDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = (IThread)Agent(UniqueId("digest-idempotent"));

        await thread.StartTaskDigestAsync("task-789", TimeSpan.FromMinutes(5), ct);
        await thread.StartTaskDigestAsync("task-789", TimeSpan.FromMinutes(5), ct);

        var jobs = await ((IAgent)thread).ListJobs(ct);
        var digestJobs = jobs.Where(j => j.Name.Contains("task-789")).ToList();
        Assert.Single(digestJobs);
    }
}
