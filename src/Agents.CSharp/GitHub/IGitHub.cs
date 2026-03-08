using Octokit;

namespace IAW.Agents.CSharp.GitHub;

public interface IGitHub
{
    IGitHubClient Client { get; }
    bool IsConfigured { get; }
}
