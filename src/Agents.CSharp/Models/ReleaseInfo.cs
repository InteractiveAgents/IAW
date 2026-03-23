namespace IAW.Agents.Coding.Models;

[GenerateSerializer]
public sealed record ReleaseInfo(
    [property: Id(0)] string TagName,
    [property: Id(1)] string Name,
    [property: Id(2)] string Body,
    [property: Id(3)] DateTimeOffset? PublishedAt);
