using Core.Contracts;

namespace IAW.Agents.CSharp;

public interface IRoslyn : IAgent
{
    Task<string> GetTypeMapAsync(CancellationToken ct = default);
    Task<string> FindReferencesAsync(string symbol, CancellationToken ct = default);
    Task<string> AnalyzeArchitectureAsync(CancellationToken ct = default);
    Task<string> DetectPatternsAsync(string patternName, CancellationToken ct = default);
    Task<string> GetDependencyGraphAsync(CancellationToken ct = default);
    Task<string> AnalyzeBuildErrorsAsync(string buildOutput, CancellationToken ct = default);
}
