using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using IAW.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.AI;
using Core.Tools;
using Core.Contracts;
using Core.Communication;
using Core.AI;
using Core.AI.Models;
using Core.Communication.Messages;

namespace IAW.Agents.Coding;

public class RoslynAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IRoslyn, IReceiver<TestResultMessage>
{
    protected override string DisplayName => "Roslyn";

    public static string AgentDescription => "Parses C# projects with Roslyn to extract type maps, detect patterns, analyze architecture, and map dependencies.";
    public static string[] AgentCapabilities => ["roslyn", "csharp", "parse", "analyze", "architecture", "refactor"];

    protected override string Instructions =>
        "You are Roslyn, the IAW team's C# code intelligence engine. " +
        "You parse projects, extract types, analyze architecture, detect patterns, and map dependencies. " +
        "Use your tools to perform analysis — return concrete findings, not descriptions of what could be analyzed.";
    protected override IReadOnlyList<AITool> DefineTools()
    {
        Func<string> getWorkspace = () => GetWorkspacePath() ?? Path.GetTempPath();
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new Tools.RoslynTools(getWorkspace));
        return tools;
    }

    public async Task<string> GetTypeMapAsync(CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set. Call SetWorkspaceAsync first.";

        var csFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.cs", ct);

        var types = new List<TypeEntry>();
        foreach (var file in csFiles)
        {
            var sourceText = await File.ReadAllTextAsync(file, ct);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, cancellationToken: ct);
            var root = await syntaxTree.GetRootAsync(ct);
            types.AddRange(ExtractTypes(root, file));
        }

        CacheTypeCatalog(types);
        await WriteStateAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Type map for {workspace} ({csFiles.Length} files, {types.Count} types):");
        foreach (var group in types.GroupBy(t => t.Namespace))
        {
            sb.AppendLine($"\n  {(string.IsNullOrEmpty(group.Key) ? "(global)" : group.Key)}:");
            foreach (var t in group)
                sb.AppendLine($"    {t.Kind} {t.Name} -- {t.Methods.Length} methods, {t.Properties.Length} properties");
        }
        return sb.ToString();
    }

    public async Task<string> FindReferencesAsync(string symbol, CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set.";

        var csFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.cs", ct);

        var results = new List<string>();
        foreach (var file in csFiles)
        {
            var sourceText = await File.ReadAllTextAsync(file, ct);
            var lines = sourceText.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(symbol, StringComparison.Ordinal))
                    results.Add($"{Path.GetRelativePath(workspace, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        return results.Count == 0
            ? $"No references found for '{symbol}'"
            : $"Found {results.Count} reference(s) for '{symbol}':\n{string.Join("\n", results)}";
    }

    public async Task<string> AnalyzeArchitectureAsync(CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set.";

        var projectFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.csproj", ct);
        var projectSummary = string.Join("\n", projectFiles.Select(f =>
            $"- {Path.GetRelativePath(workspace, f)}"));

        var (projectRefs, packageRefs) = ParseAllProjectFiles(projectFiles);

        var prompt = $"""
            Analyze this .NET solution architecture:

            Workspace: {workspace}

            Projects:
            {projectSummary}

            Project references:
            {string.Join("\n", projectRefs.Select(r => $"  {r.From} -> {r.To}"))}

            Package references:
            {string.Join("\n", packageRefs.Select(p => $"  {p.Project}: {p.Package}"))}

            Identify: 1) Layer violations, 2) Circular dependencies, 3) Architecture patterns used,
            4) Recommendations for improvement.
            """;

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
        var analysis = response.Text ?? string.Empty;

        State["architecture-analysis"] = new StateEntry("architecture-analysis", analysis);
        await WriteStateAsync(ct);

        return analysis;
    }

    public async Task<string> DetectPatternsAsync(string patternName, CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set.";

        var csFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.cs", ct);

        var sb = new StringBuilder();
        sb.AppendLine($"Pattern detection: '{patternName}' across {csFiles.Length} files");

        foreach (var file in csFiles)
        {
            var sourceText = await File.ReadAllTextAsync(file, ct);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, cancellationToken: ct);
            var root = await syntaxTree.GetRootAsync(ct);

            var matches = patternName.ToLowerInvariant() switch
            {
                "singleton" => DetectSingleton(root),
                "factory" => DetectFactory(root),
                "observer" => DetectObserver(root),
                "disposable" => DetectDisposable(root),
                "async" => DetectAsyncPatterns(root),
                _ => DetectByName(root, patternName)
            };

            if (matches.Count > 0)
            {
                sb.AppendLine($"\n  {Path.GetRelativePath(workspace, file)}:");
                foreach (var match in matches)
                    sb.AppendLine($"    {match}");
            }
        }

        return sb.ToString();
    }

    public async Task<string> GetDependencyGraphAsync(CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set.";

        var projectFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.csproj", ct);
        var sb = new StringBuilder();
        sb.AppendLine("Dependency graph:");

        foreach (var projectFile in projectFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            sb.AppendLine($"\n  {projectName}:");

            var (projectRefs, packageRefs) = ParseProjectFile(projectFile);
            foreach (var pr in projectRefs)
                sb.AppendLine($"    -> [project] {Path.GetFileNameWithoutExtension(pr)}");
            foreach (var pkg in packageRefs)
                sb.AppendLine($"    -> [nuget] {pkg}");
        }

        await Task.CompletedTask;
        return sb.ToString();
    }

    public async Task<string> AnalyzeBuildErrorsAsync(string buildOutput, CancellationToken ct = default)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User,
                $"Analyze these build errors and suggest fixes:\n\n{buildOutput}\n\n" +
                "For each error: 1) Root cause, 2) Fix, 3) Related errors that will resolve together.")
        };

        var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? string.Empty;
    }

    public async Task<MessageReceipt> ReceiveAsync(TestResultMessage message, CancellationToken ct = default)
    {
        var eventName = message.Failed == 0 ? "tests.passed" : "tests.failed";
        State[$"test-result-{DateTimeOffset.UtcNow.Ticks}"] = new StateEntry(eventName, $"{message.Passed}/{message.Total} passed");
        await WriteStateAsync(ct);
        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    public Task<bool> CanReceiveAsync(CancellationToken ct = default) => Task.FromResult(true);

    private static IEnumerable<TypeEntry> ExtractTypes(SyntaxNode root, string filePath)
    {
        var namespaceDeclarations = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>();
        foreach (var ns in namespaceDeclarations)
        {
            var namespaceName = ns.Name.ToString();
            foreach (var typeDecl in ns.DescendantNodes().OfType<TypeDeclarationSyntax>())
                yield return CreateTypeEntry(typeDecl, namespaceName, filePath);
        }

        foreach (var typeDecl in root.ChildNodes().OfType<TypeDeclarationSyntax>())
            yield return CreateTypeEntry(typeDecl, "", filePath);
    }

    private static TypeEntry CreateTypeEntry(TypeDeclarationSyntax typeDecl, string namespaceName, string filePath)
    {
        var kind = typeDecl switch
        {
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax => "record",
            StructDeclarationSyntax => "struct",
            _ => "unknown"
        };

        var methods = typeDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(m => m.Identifier.Text)
            .ToArray();

        var properties = typeDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(p => p.Identifier.Text)
            .ToArray();

        return new TypeEntry(typeDecl.Identifier.Text, namespaceName, kind, methods, properties, filePath);
    }

    private static (string[] ProjectReferences, string[] PackageReferences) ParseProjectFile(string projectPath)
    {
        try
        {
            var doc = XDocument.Load(projectPath);
            var projectRefs = doc.Descendants("ProjectReference")
                .Select(e => e.Attribute("Include")?.Value ?? "")
                .Where(v => !string.IsNullOrEmpty(v))
                .ToArray();
            var packageRefs = doc.Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value ?? "")
                .Where(v => !string.IsNullOrEmpty(v))
                .ToArray();
            return (projectRefs, packageRefs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ([], []);
        }
    }

    private static (List<ProjectRef> ProjectRefs, List<PackageRef> PackageRefs) ParseAllProjectFiles(string[] projectFiles)
    {
        var projectRefs = new List<ProjectRef>();
        var packageRefs = new List<PackageRef>();
        foreach (var pf in projectFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(pf);
            var (prs, pkgs) = ParseProjectFile(pf);
            foreach (var pr in prs)
                projectRefs.Add(new ProjectRef(projectName, Path.GetFileNameWithoutExtension(pr)));
            foreach (var pkg in pkgs)
                packageRefs.Add(new PackageRef(projectName, pkg));
        }
        return (projectRefs, packageRefs);
    }

    private void CacheTypeCatalog(List<TypeEntry> types)
    {
        State["type-catalog"] = new StateEntry("type-catalog", JsonSerializer.Serialize(types));
        State["cached-type-count"] = new StateEntry("cached-type-count", types.Count);
    }

    private static List<string> DetectSingleton(SyntaxNode root)
    {
        return [.. root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Modifiers.Any(SyntaxKind.StaticKeyword)
                     && p.Type.ToString().Contains(((TypeDeclarationSyntax?)p.Parent)?.Identifier.Text ?? ""))
            .Select(p => $"Singleton pattern: {((TypeDeclarationSyntax?)p.Parent)?.Identifier.Text}.{p.Identifier.Text}")];
    }

    private static List<string> DetectFactory(SyntaxNode root)
    {
        return [.. root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text.StartsWith("Create", StringComparison.Ordinal)
                     || m.Identifier.Text.StartsWith("Build", StringComparison.Ordinal))
            .Select(m => $"Factory method: {((TypeDeclarationSyntax?)m.Parent)?.Identifier.Text}.{m.Identifier.Text}")];
    }

    private static List<string> DetectObserver(SyntaxNode root)
    {
        return [.. root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Where(t => t.BaseList?.Types.Any(bt => bt.ToString().Contains("IObserv")) == true)
            .Select(t => $"Observer: {t.Identifier.Text}")];
    }

    private static List<string> DetectDisposable(SyntaxNode root)
    {
        return [.. root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Where(t => t.BaseList?.Types.Any(bt => bt.ToString().Contains("IDisposable")) == true)
            .Select(t => $"Disposable: {t.Identifier.Text}")];
    }

    private static List<string> DetectAsyncPatterns(SyntaxNode root)
    {
        return [.. root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Modifiers.Any(SyntaxKind.AsyncKeyword))
            .Select(m => $"Async: {((TypeDeclarationSyntax?)m.Parent)?.Identifier.Text}.{m.Identifier.Text}")];
    }

    private static List<string> DetectByName(SyntaxNode root, string patternName)
    {
        return [.. root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Where(t => t.Identifier.Text.Contains(patternName, StringComparison.OrdinalIgnoreCase)
                     || (t.BaseList?.Types.Any(bt => bt.ToString().Contains(patternName, StringComparison.OrdinalIgnoreCase)) == true))
            .Select(t => $"Match: {t.Identifier.Text}")];
    }

    private record ProjectRef(string From, string To);
    private record PackageRef(string Project, string Package);
}

[GenerateSerializer]
public record TypeEntry(
    [property: Id(0)] string Name,
    [property: Id(1)] string Namespace,
    [property: Id(2)] string Kind,
    [property: Id(3)] string[] Methods,
    [property: Id(4)] string[] Properties,
    [property: Id(5)] string FilePath);
