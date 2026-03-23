using Core.Contracts;
using IAW.Agents.Coding.Models;

namespace IAW.Agents.Coding;

public interface IGitHub : IAgent
{
    static string IAgent.AgentDisplayName => "GitHub";

    static string IAgent.AgentDescription =>
        "Monitors GitHub repositories for new releases, creates issues, and tracks project activity via the GitHub API.";

    static string[] IAgent.AgentCapabilities =>
        ["github", "releases", "issues", "repository", "monitor", "api"];

    static string IAgent.AgentInstructions =>
        "You are GitHub, the IAW team's GitHub API specialist. " +
        "You monitor repositories for releases, manage issues, and track project activity.";

    Task WatchReleases(string repo, TimeSpan checkEvery, CancellationToken ct = default);
    Task CreateIssue(string repo, string title, string body, CancellationToken ct = default);
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default);
}