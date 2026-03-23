using Core.Tools;
using IAW.Agents.CSharp.Roslyn.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.ComponentModel;
using System.Text;

namespace IAW.Agents.Coding.Tools;

public class RoslynTools(Func<string> getWorkspacePath, SolutionWorkspaceManager? workspaceManager = null)
{
    private string WorkspacePath => getWorkspacePath();

    public RoslynTools(string workspacePath) : this(() => workspacePath) { }

    [Description("Analyze C# file syntax. Returns diagnostics (errors, warnings) from Roslyn parser.")]
    public async Task<string> AnalyzeSyntaxAsync(
        [Description("Path to the C# file to analyze")] string path)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
            return $"File not found: {fullPath}";

        var source = await File.ReadAllTextAsync(fullPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = root.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToList();

        if (diagnostics.Count == 0)
            return $"No diagnostics found in {Path.GetFileName(fullPath)}";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {diagnostics.Count} diagnostic(s) in {Path.GetFileName(fullPath)}:");
        foreach (var diag in diagnostics)
        {
            var lineSpan = diag.Location.GetLineSpan();
            var severity = diag.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            sb.AppendLine($"  {severity} at line {lineSpan.StartLinePosition.Line + 1}: {diag.GetMessage()}");
        }
        return sb.ToString();
    }

    [Description("Analyze C# file semantics using full Roslyn compilation. Requires project directory context.")]
    public async Task<string> AnalyzeSemanticsAsync(
        [Description("Path to the C# file")] string path,
        [Description("Path to the project directory containing .cs files")] string projectPath)
    {
        var fullPath = ResolvePath(path);
        if (!File.Exists(fullPath))
            return $"File not found: {fullPath}";

        // use workspace compilation when available
        if (workspaceManager is { IsReady: true })
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var compilation = await workspaceManager.GetCompilationAsync(projectName);
            if (compilation is not null)
            {
                var targetTree = compilation.SyntaxTrees.FirstOrDefault(t =>
                    string.Equals(t.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
                if (targetTree is not null)
                {
                    var semanticModel = compilation.GetSemanticModel(targetTree);
                    return FormatDiagnostics(semanticModel.GetDiagnostics(), fullPath);
                }
            }
        }

        // fallback: minimal single-project compilation
        var projectDir = Directory.Exists(projectPath)
            ? projectPath
            : Path.GetDirectoryName(projectPath) ?? WorkspacePath;

        var csFiles = await WorkspaceFiles.EnumerateFilesAsync(projectDir, "*.cs");

        var trees = new List<SyntaxTree>();
        foreach (var csFile in csFiles)
        {
            var src = await File.ReadAllTextAsync(csFile);
            trees.Add(CSharpSyntaxTree.ParseText(src, path: csFile));
        }

        var fallbackCompilation = CSharpCompilation.Create("Analysis",
            syntaxTrees: trees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var fallbackTree = trees.FirstOrDefault(t => t.FilePath == fullPath);
        if (fallbackTree is null)
            return $"File {fullPath} not found in project trees";

        var fallbackModel = fallbackCompilation.GetSemanticModel(fallbackTree);
        return FormatDiagnostics(fallbackModel.GetDiagnostics(), fullPath);
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> allDiagnostics, string fullPath)
    {
        var diagnostics = allDiagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .ToList();

        if (diagnostics.Count == 0)
            return $"No semantic issues found in {Path.GetFileName(fullPath)}";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {diagnostics.Count} semantic issue(s):");
        foreach (var diag in diagnostics.Take(20))
        {
            var lineSpan = diag.Location.GetLineSpan();
            var severity = diag.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            sb.AppendLine($"  {severity} at line {lineSpan.StartLinePosition.Line + 1}: {diag.GetMessage()}");
        }
        return sb.ToString();
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(WorkspacePath, path));
    }
}