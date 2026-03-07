using IAW.Core;

namespace IAW.Agents.Infrastructure;

public interface IGit : IAgent
{
    Task<string> StatusAsync(string repoPath, CancellationToken ct = default);
    Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default);
    Task<string> DiffAsync(string repoPath, CancellationToken ct = default);
    Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default);
    Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default);
    Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record GitMetrics(
    [property: Id(0)] int TotalCommits,
    [property: Id(1)] int TotalReverts,
    [property: Id(2)] Dictionary<string, int> FileChurn,
    [property: Id(3)] DateTimeOffset LastCommit);
