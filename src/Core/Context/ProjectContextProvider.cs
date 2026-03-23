using Core.Contracts;

namespace Core.Context;

public class ProjectContextProvider(
    IList<ProjectTask> tasks,
    IDictionary<string, FileReference> files,
    IDictionary<string, string> projectMeta) : IAgentContextProvider
{
    public string Name => "project-context";

    public Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        var context = new List<string>();

        if (projectMeta.TryGetValue("description", out var description) && !string.IsNullOrEmpty(description))
            context.Add($"[project] {description}, {tasks.Count} tasks, {files.Count} files");
        else
            context.Add($"[project] {tasks.Count} tasks, {files.Count} files");

        if (files.Count > 0)
        {
            var fileList = string.Join(", ", files.Values.Select(f => f.FileName));
            context.Add($"[project files] {fileList}");
        }

        return Task.FromResult<IReadOnlyList<string>>(context);
    }
}