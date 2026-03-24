using Core.AI;
using Microsoft.Extensions.Configuration;
using Octokit;

namespace IAW.Agents.Coding.GitHub;

public class GitHubService : IGitHubService
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