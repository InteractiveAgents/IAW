using System.ComponentModel;

namespace Core.Tools;

public class WorkspaceTools(Func<string> getWorkspacePath, Action<string> setWorkspacePath)
{
    [Description("Set the workspace directory for all file, shell, and analysis operations. Call this FIRST when the user specifies a project directory.")]
    public string SetWorkspace(
        [Description("Absolute path to the workspace directory")] string path)
    {
        if (!Path.IsPathRooted(path))
            return $"Error: workspace path must be absolute. Got: {path}";

        setWorkspacePath(path);
        return $"Workspace set to: {path}";
    }

    [Description("Get the current workspace directory path.")]
    public string GetWorkspace() => getWorkspacePath();
}