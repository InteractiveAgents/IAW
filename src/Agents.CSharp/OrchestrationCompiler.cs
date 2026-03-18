using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IAW.Agents.CSharp;

public static class OrchestrationCompiler
{
    public static CompilationResult Compile(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new CompilationResult(true, []);

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "OrchestrationScript",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return new CompilationResult(
            diagnostics.Length == 0,
            [.. diagnostics.Select(d => d.GetMessage())]);
    }
}

public record CompilationResult(bool Success, string[] Errors);
