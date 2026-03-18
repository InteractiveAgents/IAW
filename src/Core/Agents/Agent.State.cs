using Core.Contracts;

namespace IAW.Core;

public abstract partial class Agent
{
    private const string WorkspacePathKey = "workspace-path";

    public async Task SetWorkspace(string path, CancellationToken ct = default)
    {
        durableState.State[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path);
        await WriteStateAsync(ct);
    }

    public Task<AgentState> GetState(CancellationToken ct = default)
    {
        var entries = new Dictionary<string, StateEntry>();
        foreach (var kvp in durableState.State)
            entries[kvp.Key] = kvp.Value;
        return Task.FromResult(new AgentState(entries));
    }

    protected string? GetWorkspacePath()
        => durableState.State.TryGetValue(WorkspacePathKey, out var entry)
            ? entry.Value.ToString()
            : null;

    protected void ValidatePathWithinWorkspace(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        var workspace = GetWorkspacePath();
        if (workspace is null) return;

        var fullPath = Path.GetFullPath(path);
        var fullWorkspace = Path.GetFullPath(workspace);
        var workspaceWithSeparator = fullWorkspace.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? fullWorkspace
            : fullWorkspace + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(workspaceWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.Equals(fullWorkspace, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path '{path}' is outside the workspace '{workspace}'.");
    }
}
