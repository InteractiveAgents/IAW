using IAW.Core;
using IAW.Agents.CSharp.Models;

namespace IAW.Agents.CSharp;

public interface INuGet : IAgent
{
    Task WatchPackagesAsync(string directoryPackagesPropsPath, TimeSpan checkEvery, CancellationToken ct = default);
    Task<IReadOnlyList<PackageUpdate>> GetOutdatedAsync(CancellationToken ct = default);
}
