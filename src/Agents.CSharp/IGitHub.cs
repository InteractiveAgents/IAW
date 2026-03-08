using IAW.Core;

namespace IAW.Agents.CSharp;

public interface IGitHub : IAgent
{
    Task WatchReleases(string repo, TimeSpan checkEvery, CancellationToken ct = default);
    Task CreateIssue(string repo, string title, string body, CancellationToken ct = default);
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default);
}
