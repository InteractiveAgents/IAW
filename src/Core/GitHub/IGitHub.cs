using Octokit;

namespace Core.GitHub;

public interface IGitHub
{
    IGitHubClient Client { get; }
    bool IsConfigured { get; }
}
