using Core.Contracts;

namespace IAW.Agents.Infrastructure;

public interface IAspire : IAgent
{
    Task<ResourceStatus[]> ListResourcesAsync(CancellationToken ct = default);
    Task RestartResourceAsync(string resourceName, CancellationToken ct = default);
    Task StopResourceAsync(string resourceName, CancellationToken ct = default);
    Task StartResourceAsync(string resourceName, CancellationToken ct = default);
    Task<string[]> GetLogsAsync(string resourceName, CancellationToken ct = default);
    Task<AspireMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record ResourceStatus(
    [property: Id(0)] string Name,
    [property: Id(1)] string State,
    [property: Id(2)] string Type,
    [property: Id(3)] string[] Endpoints);

[GenerateSerializer]
public record AspireMetrics(
    [property: Id(0)] int TotalRestarts,
    [property: Id(1)] Dictionary<string, int> RestartCounts,
    [property: Id(2)] Dictionary<string, TimeSpan> ResourceUptime,
    [property: Id(3)] DateTimeOffset LastHealthCheck);
