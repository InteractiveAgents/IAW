using IAW.Core;
using IAW.Core.AI.Models;
using IAW.Agents.CSharp.Models;
using IAW.Core.AI;
using IAW.Core.Attributes;
using Microsoft.Extensions.AI;
using Octokit;
using Orleans.Journaling;

namespace IAW.Agents.CSharp;

[DevVisible("GitHub integration -- release monitoring, issue creation, repository management")]
public class GitHubAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<Core.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    IGitHubClient gitHubClient)
    : Agent(state, eventLog, chatClient, history, trackingItems), IGitHub
{
    protected override string DisplayName => "GitHub Monitor";
    protected override string Instructions => "Monitors GitHub repositories for new releases and manages issues";

    public async Task WatchReleases(string repo, TimeSpan checkEvery, CancellationToken ct = default)
    {
        var parts = repo.Split('/');
        if (parts.Length != 2) throw new ArgumentException("Repo must be in 'owner/name' format", nameof(repo));

        State["repo"] = new StateEntry("repo", repo);
        State["interval-ticks"] = new StateEntry("interval-ticks", checkEvery.Ticks);

        try
        {
            var latest = await gitHubClient.Repository.Release.GetLatest(parts[0], parts[1]);
            State["last-tag"] = new StateEntry("last-tag", latest.TagName);
        }
        catch (NotFoundException)
        {
            State["last-tag"] = new StateEntry("last-tag", string.Empty);
        }

        await WriteStateAsync(ct);

        await this.RegisterOrUpdateReminder("check-releases", checkEvery, checkEvery);
    }

    public async Task CreateIssue(string repo, string title, string body, CancellationToken ct = default)
    {
        var parts = repo.Split('/');
        if (parts.Length != 2) throw new ArgumentException("Repo must be in 'owner/name' format", nameof(repo));

        await gitHubClient.Issue.Create(parts[0], parts[1], new NewIssue(title) { Body = body });
    }

    public Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        if (!State.TryGetValue("last-tag", out var tagEntry)
            || string.IsNullOrEmpty(tagEntry.Value.ToString()))
            return Task.FromResult<ReleaseInfo?>(null);

        var tag = tagEntry.Value.ToString()!;
        return Task.FromResult<ReleaseInfo?>(new ReleaseInfo(tag, tag, string.Empty, null));
    }

    public override async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != "check-releases") return;

        if (!State.TryGetValue("repo", out var repoEntry))
            return;

        var repo = repoEntry.Value.ToString()!;
        var parts = repo.Split('/');
        var lastTag = State.TryGetValue("last-tag", out var tagEntry)
            ? tagEntry.Value.ToString()!
            : string.Empty;

        try
        {
            var latest = await gitHubClient.Repository.Release.GetLatest(parts[0], parts[1]);

            if (!string.IsNullOrEmpty(latest.TagName) && latest.TagName != lastTag)
            {
                State["last-tag"] = new StateEntry("last-tag", latest.TagName);
                await WriteStateAsync();

                await PublishAsync("github.release", new Dictionary<string, object>
                {
                    ["Repo"] = repo,
                    ["TagName"] = latest.TagName,
                    ["Name"] = latest.Name ?? string.Empty,
                    ["Body"] = latest.Body ?? string.Empty,
                    ["PublishedAt"] = latest.PublishedAt?.ToString("O") ?? string.Empty
                });
            }
        }
        catch (NotFoundException)
        {
            // no releases yet
        }
    }
}
