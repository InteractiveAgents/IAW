using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace IAW.Agents.CSharp.Roslyn.Workspace;

public sealed class SolutionWorkspaceManager : IDisposable
{
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private readonly ConcurrentDictionary<string, Compilation> _compilationCache = new();

    public bool IsReady => _solution is not null;
    public Solution? Solution => _solution;

    public async Task LoadSolutionAsync(string solutionPath, CancellationToken ct = default)
    {
        _compilationCache.Clear();
        _workspace?.Dispose();

        _workspace = MSBuildWorkspace.Create();
        _solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct);
    }

    public Compilation? GetCompilation(string projectName)
    {
        if (_solution is null) return null;
        if (_compilationCache.TryGetValue(projectName, out var cached)) return cached;

        var project = _solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        if (project is null) return null;

        var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
        if (compilation is not null)
            _compilationCache[projectName] = compilation;
        return compilation;
    }

    public async Task<Compilation?> GetCompilationAsync(string projectName, CancellationToken ct = default)
    {
        if (_solution is null) return null;
        if (_compilationCache.TryGetValue(projectName, out var cached)) return cached;

        var project = _solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        if (project is null) return null;

        var compilation = await project.GetCompilationAsync(ct);
        if (compilation is not null)
            _compilationCache[projectName] = compilation;
        return compilation;
    }

    public IEnumerable<string> GetProjectNames() =>
        _solution?.Projects.Select(p => p.Name) ?? [];

    public async Task ReloadAsync(string solutionPath, CancellationToken ct = default)
    {
        await LoadSolutionAsync(solutionPath, ct);
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}
