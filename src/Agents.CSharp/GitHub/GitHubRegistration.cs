using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IAW.Agents.CSharp.GitHub;

public static class GitHubRegistration
{
    public static IHostApplicationBuilder AddGitHubClient(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IGitHubService, GitHubService>();
        return builder;
    }
}
