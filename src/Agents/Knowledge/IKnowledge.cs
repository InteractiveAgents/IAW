using IAW.Core;

namespace IAW.Agents.Knowledge;

public interface IKnowledge : IAgent
{
    Task SetProjectInfo(ProjectInfo info);
    Task<ProjectInfo?> GetProjectInfo();
    Task AddDecision(string title, string rationale, string outcome);
    Task<IReadOnlyList<ProjectDecision>> GetDecisions();
    Task SetTechStack(IReadOnlyList<string> technologies);
    Task<IReadOnlyList<string>> GetTechStack();
    Task AddPattern(string name, string description, string? example = null);
    Task<IReadOnlyList<ProjectPattern>> GetPatterns();
    Task AddConvention(string convention);
    Task<IReadOnlyList<string>> GetConventions();
    Task SetFileStructure(string treeOutput);
    Task<string?> GetFileStructure();
}

[GenerateSerializer]
public record ProjectInfo(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description,
    [property: Id(2)] string[] Goals,
    [property: Id(3)] string Status);

[GenerateSerializer]
public record ProjectDecision(
    [property: Id(0)] string Title,
    [property: Id(1)] string Rationale,
    [property: Id(2)] string Outcome,
    [property: Id(3)] DateTimeOffset Timestamp);

[GenerateSerializer]
public record ProjectPattern(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description,
    [property: Id(2)] string? Example);
