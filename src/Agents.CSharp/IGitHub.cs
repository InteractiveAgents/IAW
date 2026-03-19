using IAW.Agents.Coding.Models;
using Core.Contracts;

namespace IAW.Agents.Coding;

public interface IGitHub : IAgent
{
    Task WatchReleases(string repo, TimeSpan checkEvery, CancellationToken ct = default);
    Task CreateIssue(string repo, string title, string body, CancellationToken ct = default);
    Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default);
}
