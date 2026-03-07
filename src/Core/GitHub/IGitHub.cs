using Octokit;

namespace IAW.Core.GitHub;

public interface IGitHub
{
    IGitHubClient Client { get; }
    bool IsConfigured { get; }
}
