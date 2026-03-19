using Octokit;

namespace IAW.Agents.Coding.GitHub;

public interface IGitHubService
{
    IGitHubClient Client { get; }
    bool IsConfigured { get; }
}
