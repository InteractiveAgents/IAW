using IAW.Core.AI;
using Microsoft.Extensions.Configuration;
using Octokit;

namespace IAW.Core.GitHub;

public class GitHubService : IGitHub
{
    public IGitHubClient Client { get; }
    public bool IsConfigured { get; }

    public GitHubService(IConfiguration config)
    {
        var token = config[LlmConfig.GitHubToken];
        IsConfigured = !string.IsNullOrEmpty(token);
        Client = new GitHubClient(new ProductHeaderValue("IAW"))
        {
            Credentials = IsConfigured ? new Credentials(token) : Credentials.Anonymous
        };
    }
}
