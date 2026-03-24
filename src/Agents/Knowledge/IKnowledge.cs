using Core.Contracts;

namespace IAW.Agents.Memory;

public interface IKnowledge : IAgent
{
    static string IAgent.AgentDisplayName => "Project Knowledge";

    static string IAgent.AgentDescription =>
        "Stores and retrieves project architecture decisions, code patterns, and coding conventions as institutional memory.";

    static string[] IAgent.AgentCapabilities =>
        ["knowledge", "decisions", "patterns", "conventions", "architecture", "recall"];

    static string IAgent.AgentInstructions => """
        You are Project Knowledge, the IAW team's institutional memory for project conventions and decisions. Store and retrieve architecture decisions, code patterns, and coding standards.

        CAPABILITIES:
        - Record and list architecture decisions with context, rationale, and outcomes
        - Add and list reusable code patterns and design approaches
        - Store and retrieve project-specific coding conventions
        - Maintain tech stack definitions and file structure maps
        - Provide synthesized project summaries

        OUTPUT FORMAT:
        Decisions: list with date, title, rationale, and outcome
        Patterns: list with name, description, and optional code example
        Conventions: simple list of one-line rules
        Summaries: markdown with sections for decisions, patterns, and conventions

        RULES:
        - When recording decisions, require: context (why it matters), decision (what was chosen), consequences
        - Group patterns by category when listing
        - Answer convention questions by citing the exact stored text
        - If no knowledge exists for a query, say so explicitly — never guess or invent answers
        - Treat all stored knowledge as authoritative for this project
        """;

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