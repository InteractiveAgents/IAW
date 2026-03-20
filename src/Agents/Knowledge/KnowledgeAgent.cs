using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Memory;

public class KnowledgeAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IKnowledge
{
    protected override string DisplayName => "Project Knowledge";

    public static string AgentDescription => "Stores and retrieves project architecture decisions, code patterns, and coding conventions as institutional memory.";
    public static string[] AgentCapabilities => ["knowledge", "decisions", "patterns", "conventions", "architecture", "recall"];

    protected override string Instructions => """
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

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(RecordDecisionTool),
            AIFunctionFactory.Create(ListDecisionsTool),
            AIFunctionFactory.Create(AddPatternTool),
            AIFunctionFactory.Create(ListPatternsTool),
            AIFunctionFactory.Create(AddConventionTool),
            AIFunctionFactory.Create(ListConventionsTool),
            AIFunctionFactory.Create(GetProjectSummaryTool)
        ];
    }

    public async Task SetProjectInfo(ProjectInfo info)
    {
        State["project-info"] = new StateEntry("project-info", JsonSerializer.Serialize(info));
        await WriteStateAsync();
        await PublishAsync("project.info.updated", new Dictionary<string, object>
        {
            ["Name"] = info.Name
        });
    }

    public Task<ProjectInfo?> GetProjectInfo()
    {
        if (!State.TryGetValue("project-info", out var desc))
            return Task.FromResult<ProjectInfo?>(null);
        return Task.FromResult(JsonSerializer.Deserialize<ProjectInfo>(desc.Value.ToString()!));
    }

    public async Task AddDecision(string title, string rationale, string outcome)
    {
        var decisions = DeserializeList<ProjectDecision>("decisions");
        decisions.Add(new ProjectDecision(title, rationale, outcome, DateTimeOffset.UtcNow));
        State["decisions"] = new StateEntry("decisions", JsonSerializer.Serialize(decisions));
        await WriteStateAsync();
        await PublishAsync("decision.recorded", new Dictionary<string, object> { ["Title"] = title });
    }

    public Task<IReadOnlyList<ProjectDecision>> GetDecisions()
        => Task.FromResult<IReadOnlyList<ProjectDecision>>(DeserializeList<ProjectDecision>("decisions"));

    public async Task SetTechStack(IReadOnlyList<string> technologies)
    {
        State["tech-stack"] = new StateEntry("tech-stack", JsonSerializer.Serialize(technologies));
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> GetTechStack()
        => Task.FromResult<IReadOnlyList<string>>(DeserializeList<string>("tech-stack"));

    public async Task AddPattern(string name, string description, string? example = null)
    {
        var patterns = DeserializeList<ProjectPattern>("patterns");
        patterns.Add(new ProjectPattern(name, description, example));
        State["patterns"] = new StateEntry("patterns", JsonSerializer.Serialize(patterns));
        await WriteStateAsync();
        await PublishAsync("pattern.added", new Dictionary<string, object> { ["Name"] = name });
    }

    public Task<IReadOnlyList<ProjectPattern>> GetPatterns()
        => Task.FromResult<IReadOnlyList<ProjectPattern>>(DeserializeList<ProjectPattern>("patterns"));

    public async Task AddConvention(string convention)
    {
        var conventions = DeserializeList<string>("conventions");
        conventions.Add(convention);
        State["conventions"] = new StateEntry("conventions", JsonSerializer.Serialize(conventions));
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> GetConventions()
        => Task.FromResult<IReadOnlyList<string>>(DeserializeList<string>("conventions"));

    public async Task SetFileStructure(string treeOutput)
    {
        State["file-structure"] = new StateEntry("file-structure", treeOutput);
        await WriteStateAsync();
    }

    public Task<string?> GetFileStructure()
    {
        return State.TryGetValue("file-structure", out var desc)
            ? Task.FromResult<string?>(desc.Value.ToString())
            : Task.FromResult<string?>(null);
    }

    private List<T> DeserializeList<T>(string stateKey)
    {
        if (!State.TryGetValue(stateKey, out var desc))
            return [];
        try { return JsonSerializer.Deserialize<List<T>>(desc.Value.ToString()!) ?? []; }
        catch { return []; }
    }

    [Description("Record a new architectural decision for this project")]
    private async Task<string> RecordDecisionTool(
        [Description("Decision title")] string title,
        [Description("Why this decision was made")] string rationale,
        [Description("The chosen outcome")] string outcome)
    {
        await AddDecision(title, rationale, outcome);
        return $"Decision recorded: {title}";
    }

    [Description("List all recorded project decisions")]
    private async Task<string> ListDecisionsTool()
    {
        var decisions = await GetDecisions();
        if (decisions.Count == 0) return "No decisions recorded yet.";
        var sb = new StringBuilder();
        foreach (var d in decisions)
            sb.AppendLine($"- [{d.Timestamp:yyyy-MM-dd}] {d.Title}: {d.Rationale} -> {d.Outcome}");
        return sb.ToString();
    }

    [Description("Add a design pattern used in this project")]
    private async Task<string> AddPatternTool(
        [Description("Pattern name")] string name,
        [Description("What the pattern does and when to use it")] string description,
        [Description("Code example (optional)")] string? example = null)
    {
        await AddPattern(name, description, example);
        return $"Pattern added: {name}";
    }

    [Description("List all design patterns for this project")]
    private async Task<string> ListPatternsTool()
    {
        var patterns = await GetPatterns();
        if (patterns.Count == 0) return "No patterns recorded yet.";
        var sb = new StringBuilder();
        foreach (var p in patterns)
        {
            sb.AppendLine($"- {p.Name}: {p.Description}");
            if (p.Example is not null) sb.AppendLine($"  Example: {p.Example}");
        }
        return sb.ToString();
    }

    [Description("Add a coding convention for this project")]
    private async Task<string> AddConventionTool(
        [Description("The convention to follow")] string convention)
    {
        await AddConvention(convention);
        return $"Convention added: {convention}";
    }

    [Description("List all coding conventions for this project")]
    private async Task<string> ListConventionsTool()
    {
        var conventions = await GetConventions();
        if (conventions.Count == 0) return "No conventions recorded yet.";
        var sb = new StringBuilder();
        foreach (var c in conventions)
            sb.AppendLine($"- {c}");
        return sb.ToString();
    }

    [Description("Get a full summary of this project's knowledge")]
    private async Task<string> GetProjectSummaryTool()
    {
        var sb = new StringBuilder();

        var info = await GetProjectInfo();
        if (info is not null)
        {
            sb.AppendLine($"# {info.Name}");
            sb.AppendLine(info.Description);
            sb.AppendLine($"Status: {info.Status}");
            sb.AppendLine($"Goals: {string.Join(", ", info.Goals)}");
        }

        var stack = await GetTechStack();
        if (stack.Count > 0)
            sb.AppendLine($"\nTech Stack: {string.Join(", ", stack)}");

        var decisions = await GetDecisions();
        if (decisions.Count > 0)
        {
            sb.AppendLine($"\n## Decisions ({decisions.Count})");
            foreach (var d in decisions)
                sb.AppendLine($"- {d.Title}: {d.Outcome}");
        }

        var patterns = await GetPatterns();
        if (patterns.Count > 0)
        {
            sb.AppendLine($"\n## Patterns ({patterns.Count})");
            foreach (var p in patterns)
                sb.AppendLine($"- {p.Name}: {p.Description}");
        }

        var conventions = await GetConventions();
        if (conventions.Count > 0)
        {
            sb.AppendLine($"\n## Conventions ({conventions.Count})");
            foreach (var c in conventions)
                sb.AppendLine($"- {c}");
        }

        return sb.Length > 0 ? sb.ToString() : "No knowledge stored for this project yet.";
    }
}
