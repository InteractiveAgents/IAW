using Core.Contracts;

namespace IAW.Agents.Coding;

public interface IRoslyn : IAgent
{
    static string IAgent.AgentDisplayName => "Roslyn";

    static string IAgent.AgentDescription =>
        "Parses C# projects with Roslyn to extract type maps, detect patterns, analyze architecture, and map dependencies.";

    static string[] IAgent.AgentCapabilities =>
        ["roslyn", "csharp", "parse", "analyze", "architecture", "refactor"];

    static string IAgent.AgentInstructions =>
        "You are Roslyn, the IAW team's C# code intelligence engine. " +
        "You parse projects, extract types, analyze architecture, detect patterns, and map dependencies. " +
        "Use your tools to perform analysis — return concrete findings, not descriptions of what could be analyzed.";

    Task<string> GetTypeMapAsync(CancellationToken ct = default);
    Task<string> FindReferencesAsync(string symbol, CancellationToken ct = default);
    Task<string> AnalyzeArchitectureAsync(CancellationToken ct = default);
    Task<string> DetectPatternsAsync(string patternName, CancellationToken ct = default);
    Task<string> GetDependencyGraphAsync(CancellationToken ct = default);
    Task<string> AnalyzeBuildErrorsAsync(string buildOutput, CancellationToken ct = default);
}
