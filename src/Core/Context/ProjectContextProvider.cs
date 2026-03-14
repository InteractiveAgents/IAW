using Core.Contracts;

namespace Core.Context;

public class ProjectContextProvider(
    IList<ProjectTask> tasks,
    IDictionary<string, FileReference> files) : IAgentContextProvider
{
    public string Name => "project-context";

    public Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var context = new List<string>();
            context.Add($"[project] {tasks.Count} tasks, {files.Count} files");

            if (files.Count > 0)
            {
                var fileList = string.Join(", ", files.Values.Select(f => f.FileName));
                context.Add($"[project files] {fileList}");
            }

            return Task.FromResult<IReadOnlyList<string>>(context);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }
}
