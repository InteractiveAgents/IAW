using Octokit;

namespace IAW.Agents.CSharp.GitHub;

public interface IGitHubService
{
    IGitHubClient Client { get; }
    bool IsConfigured { get; }
}
