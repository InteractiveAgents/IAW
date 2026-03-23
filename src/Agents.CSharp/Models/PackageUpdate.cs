namespace IAW.Agents.Coding.Models;

[GenerateSerializer]
public sealed record PackageUpdate(
    [property: Id(0)] string PackageId,
    [property: Id(1)] string CurrentVersion,
    [property: Id(2)] string LatestVersion);
