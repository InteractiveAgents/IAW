using Core.Contracts;

namespace IAW.Agents.Coding;

public interface IRoslyn : IAgent
{
    static string IAgent.AgentDisplayName => "Roslyn";

    static string IAgent.AgentDescription =>
        "Full solution-aware C# code intelligence engine. " +
        "Loads MSBuild workspaces, builds call graphs and inheritance trees, " +
        "and performs semantic analysis across entire solutions.";

    static string[] IAgent.AgentCapabilities =>
        ["roslyn", "csharp", "parse", "analyze", "architecture", "refactor", "call-graph", "inheritance"];

    static string IAgent.AgentInstructions =>
        "You are Roslyn, the IAW team's C# code intelligence engine. " +
        "You load full solutions via MSBuild, build call graphs and inheritance trees, " +
        "and perform deep semantic analysis across projects. " +
        "Use GetWorkspaceStatusAsync to check if the workspace is loaded. " +
        "Use call graph queries (GetCallersOfAsync, GetCalleesOfAsync) for method-level analysis. " +
        "Use inheritance queries (GetImplementorsAsync, GetBaseTypesAsync, GetOverridesAsync) for type hierarchy analysis. " +
        "Return concrete findings, not descriptions of what could be analyzed.";

    Task<string> GetTypeMapAsync(CancellationToken ct = default);
    Task<string> FindReferencesAsync(string symbol, CancellationToken ct = default);
    Task<string> AnalyzeArchitectureAsync(CancellationToken ct = default);
    Task<string> DetectPatternsAsync(string patternName, CancellationToken ct = default);
    Task<string> GetDependencyGraphAsync(CancellationToken ct = default);
    Task<string> AnalyzeBuildErrorsAsync(string buildOutput, CancellationToken ct = default);

    Task<string> GetCallersOfAsync(string methodName, CancellationToken ct = default);
    Task<string> GetCalleesOfAsync(string methodName, CancellationToken ct = default);
    Task<string> GetImplementorsAsync(string interfaceName, CancellationToken ct = default);
    Task<string> GetBaseTypesAsync(string className, CancellationToken ct = default);
    Task<string> GetOverridesAsync(string methodName, CancellationToken ct = default);
    Task<string> GetWorkspaceStatusAsync(CancellationToken ct = default);
    Task<string> ImplementInterfaceAsync(string filePath, string className, string interfaceName, CancellationToken ct = default);
}