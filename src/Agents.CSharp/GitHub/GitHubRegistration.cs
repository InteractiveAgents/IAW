using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IAW.Agents.Coding.GitHub;

public static class GitHubRegistration
{
    public static IHostApplicationBuilder AddGitHubClient(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IGitHubService, GitHubService>();
        return builder;
    }
}