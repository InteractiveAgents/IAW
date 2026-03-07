namespace IAW.Core;

public abstract partial class Agent
{
    private const string WorkspacePathKey = "workspace-path";

    public async Task SetWorkspaceAsync(string path, CancellationToken ct = default)
    {
        state[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path);
        await WriteStateAsync(ct);
    }

    public Task<AgentState> GetStateAsync(CancellationToken ct = default)
    {
        var entries = new Dictionary<string, StateEntry>();
        foreach (var kvp in state)
            entries[kvp.Key] = kvp.Value;
        return Task.FromResult(new AgentState(entries));
    }

    protected string? GetWorkspacePath()
        => state.TryGetValue(WorkspacePathKey, out var entry)
            ? entry.Value.ToString()
            : null;

    protected void ValidatePathWithinWorkspace(string path)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null) return;
        var fullPath = Path.GetFullPath(path);
        var fullWorkspace = Path.GetFullPath(workspace);
        if (!fullPath.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path '{path}' is outside the workspace '{workspace}'.");
    }
}
