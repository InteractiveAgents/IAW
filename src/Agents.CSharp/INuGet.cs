using IAW.Agents.Coding.Models;
using Core.Contracts;

namespace IAW.Agents.Coding;

public interface INuGet : IAgent
{
    Task WatchPackagesAsync(string directoryPackagesPropsPath, TimeSpan checkEvery, CancellationToken ct = default);
    Task<IReadOnlyList<PackageUpdate>> GetOutdatedAsync(CancellationToken ct = default);
}
