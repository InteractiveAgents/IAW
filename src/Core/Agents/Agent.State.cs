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

    protected string ResolvePathAgainstWorkspace(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var workspace = GetWorkspacePath();
        return workspace is not null
            ? Path.GetFullPath(Path.Combine(workspace, path))
            : Path.GetFullPath(path);
    }

}
