using Core.Contracts;
using IAW.Agents.Coding.Models;

namespace IAW.Agents.Coding;

public interface INuGet : IAgent
{
    static string IAgent.AgentDisplayName => "NuGet";

    static string IAgent.AgentDescription =>
        "Monitors NuGet packages for new versions via Directory.Packages.props and tracks dependency update availability.";

    static string[] IAgent.AgentCapabilities =>
        ["nuget", "packages", "dependencies", "versions", "update", "monitor"];

    static string IAgent.AgentInstructions =>
        "You are NuGet, the IAW team's package management specialist. " +
        "You monitor packages for new versions via Directory.Packages.props and manage dependency updates.";

    Task WatchPackagesAsync(string directoryPackagesPropsPath, TimeSpan checkEvery, CancellationToken ct = default);
    Task<IReadOnlyList<PackageUpdate>> GetOutdatedAsync(CancellationToken ct = default);
}