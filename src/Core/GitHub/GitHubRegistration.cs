using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Core.GitHub;

public static class GitHubRegistration
{
    public static IHostApplicationBuilder AddGitHubClient(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IGitHub, GitHubService>();
        return builder;
    }
}
