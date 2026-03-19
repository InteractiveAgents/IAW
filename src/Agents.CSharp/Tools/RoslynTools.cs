using System.ComponentModel;
using System.Text;
using Core.Tools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IAW.Agents.Coding.Tools;

public class RoslynTools(Func<string> getWorkspacePath)
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

        var compilation = CSharpCompilation.Create("Analysis",
            syntaxTrees: trees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var targetTree = trees.FirstOrDefault(t => t.FilePath == fullPath);
        if (targetTree is null)
            return $"File {fullPath} not found in project trees";

        var semanticModel = compilation.GetSemanticModel(targetTree);
        var diagnostics = semanticModel.GetDiagnostics()
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
