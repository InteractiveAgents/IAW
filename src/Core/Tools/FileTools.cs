using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Core.Tools;

public class FileTools(Func<string> getWorkspacePath)
{
    private const int MaxResults = 500;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults", "packages"
    };

    private string WorkspacePath => getWorkspacePath();

    public FileTools(string workspacePath) : this(() => workspacePath) { }

    [Description("Read a file from the workspace")]
    public async Task<string> ReadFileAsync(
        [Description("Absolute or workspace-relative path")] string path)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
            return $"File not found: {fullPath}";
        return await File.ReadAllTextAsync(fullPath);
    }

    [Description("Create or overwrite a file")]
    public async Task<string> WriteFileAsync(
        [Description("Absolute or workspace-relative path")] string path,
        [Description("Content to write")] string content)
    {
        var fullPath = ResolvePath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, content);
        return $"File written: {fullPath}";
    }

    [Description("List files matching a glob pattern")]
    public string[] ListFiles(
        [Description("Directory to search")] string directory,
        [Description("Glob pattern like *.cs")] string pattern = "*")
    {
        var fullPath = ResolvePath(directory);
        if (!Directory.Exists(fullPath))
            return [$"Directory not found: {fullPath}"];
        return [.. EnumerateFiles(fullPath, pattern)
            .Select(f => Path.GetRelativePath(WorkspacePath, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)];
    }

    [Description("Search for a regex pattern in files")]
    public string[] SearchCode(
        [Description("Regex pattern")] string pattern,
        [Description("Directory to search")] string directory,
        [Description("File filter like *.cs")] string fileFilter = "*.cs")
    {
        var fullPath = ResolvePath(directory);
        if (!Directory.Exists(fullPath))
            return [$"Directory not found: {fullPath}"];
        var regex = new Regex(pattern, RegexOptions.Compiled);
        var matches = new List<string>();
        foreach (var file in EnumerateFiles(fullPath, fileFilter))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!regex.IsMatch(lines[i])) continue;
                matches.Add($"{Path.GetRelativePath(WorkspacePath, file)}:{i + 1}: {lines[i].Trim()}");
                if (matches.Count >= MaxResults) return [.. matches];
            }
        }
        return [.. matches];
    }

    private string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(WorkspacePath, path));

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var f in files) yield return f;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var d in dirs)
                if (!ExcludedDirectories.Contains(Path.GetFileName(d)))
                    pending.Push(d);
        }
    }
}
