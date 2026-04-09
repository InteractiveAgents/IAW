using Core.Communication;
using Core.Communication.Messages;
using Core.Contracts;
using Core.Tools;
using IAW.Agents.CSharp.Roslyn.Workspace;
using IAW.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.AI;
using Orleans.Concurrency;
using Orleans.Streams;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace IAW.Agents.Coding;

[Reentrant]
public class RoslynAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent<IRoslyn>(durableState, chatClient), IRoslyn, IReceiver<TestResultMessage>, IStreamConsumer<CodeChangedMessage>
{
    private SolutionWorkspaceManager? _workspaceManager;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        _ = LoadWorkspaceInBackground(cancellationToken);
    }

    protected override IReadOnlyList<AITool> DefineTools()
    {
        Func<string> getWorkspace = () => GetWorkspacePath() ?? Path.GetTempPath();
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new Tools.RoslynTools(getWorkspace, _workspaceManager));
        RegisterToolMethods(tools, new Tools.CodeModificationTools(getWorkspace));
        RegisterToolMethods(tools, new Tools.RefactoringTools(getWorkspace, _workspaceManager));
        return tools;
    }

    public async Task<string> GetTypeMapAsync(CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set. Call SetWorkspaceAsync first.";

        // try workspace compilations first
        if (_workspaceManager is { IsReady: true })
        {
            var types = new List<TypeEntry>();
            foreach (var projectName in _workspaceManager.GetProjectNames())
            {
                var compilation = await _workspaceManager.GetCompilationAsync(projectName, ct);
                if (compilation is null) continue;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync(ct);
                    types.AddRange(ExtractTypes(root, tree.FilePath));
                }
            }

            CacheTypeCatalog(types);
            await WriteStateAsync(ct);
            return FormatTypeMap(workspace, types, "workspace compilations");
        }

        // fallback to syntax-only parsing
        var csFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.cs", ct);
        var fallbackTypes = new List<TypeEntry>();
        foreach (var file in csFiles)
        {
            var sourceText = await File.ReadAllTextAsync(file, ct);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, cancellationToken: ct);
            var root = await syntaxTree.GetRootAsync(ct);
            fallbackTypes.AddRange(ExtractTypes(root, file));
        }

        CacheTypeCatalog(fallbackTypes);
        await WriteStateAsync(ct);
        return FormatTypeMap(workspace, fallbackTypes, $"{csFiles.Length} files (syntax-only)");
    }

    public async Task<string> FindReferencesAsync(string symbol, CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set.";

        // when workspace is ready, use SymbolFinder across all projects
        if (_workspaceManager is { IsReady: true, Solution: not null })
        {
            var results = new List<string>();
            foreach (var project in _workspaceManager.Solution.Projects)
            {
                var compilation = await project.GetCompilationAsync(ct);
                if (compilation is null) continue;

                var matchingSymbols = compilation.GetSymbolsWithName(
                    name => name.Contains(symbol, StringComparison.OrdinalIgnoreCase),
                    SymbolFilter.All, ct);

                foreach (var sym in matchingSymbols)
                {
                    var refs = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
                        .FindReferencesAsync(sym, _workspaceManager.Solution, ct);

                    foreach (var refGroup in refs)
                    {
                        foreach (var loc in refGroup.Locations)
                        {
                            var lineSpan = loc.Location.GetLineSpan();
                            var filePath = lineSpan.Path;
                            var line = lineSpan.StartLinePosition.Line + 1;
                            results.Add($"{Path.GetRelativePath(workspace, filePath)}:{line} [{sym.Kind}] {sym.Name}");
                        }
                    }
                }
            }

            if (results.Count > 0)
                return $"Found {results.Count} semantic reference(s) for '{symbol}':\n{string.Join("\n", results.Distinct())}";
        }

        // fallback to string search
        var csFiles = await WorkspaceFiles.EnumerateFilesAsync(workspace, "*.cs", ct);
        var fallbackResults = new List<string>();
        foreach (var file in csFiles)
        {
            var sourceText = await File.ReadAllTextAsync(file, ct);
            var lines = sourceText.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(symbol, StringComparison.Ordinal))
                    fallbackResults.Add($"{Path.GetRelativePath(workspace, file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        return fallbackResults.Count == 0
            ? $"No references found for '{symbol}'"
            : $"Found {fallbackResults.Count} reference(s) for '{symbol}':\n{string.Join("\n", fallbackResults)}";
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

    public Task<string> GetCallersOfAsync(string methodName, CancellationToken ct = default)
    {
        if (!State.TryGetValue("reverse-call-graph", out var entry))
            return Task.FromResult("Workspace not loaded — call graph not available. Set a workspace and wait for indexing.");

        var reverseGraph = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(entry.Value.ToString()!);
        if (reverseGraph is null)
            return Task.FromResult("Failed to deserialize reverse call graph.");

        var matches = reverseGraph
            .Where(kvp => kvp.Key.Contains(methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return Task.FromResult($"No callers found for '{methodName}'.");

        var sb = new StringBuilder();
        sb.AppendLine($"Callers of methods matching '{methodName}':");
        foreach (var (method, callers) in matches)
        {
            sb.AppendLine($"\n  {method}:");
            foreach (var caller in callers)
                sb.AppendLine($"    <- {caller}");
        }
        return Task.FromResult(sb.ToString());
    }

    public Task<string> GetCalleesOfAsync(string methodName, CancellationToken ct = default)
    {
        if (!State.TryGetValue("call-graph", out var entry))
            return Task.FromResult("Workspace not loaded — call graph not available. Set a workspace and wait for indexing.");

        var callGraph = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(entry.Value.ToString()!);
        if (callGraph is null)
            return Task.FromResult("Failed to deserialize call graph.");

        var matches = callGraph
            .Where(kvp => kvp.Key.Contains(methodName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return Task.FromResult($"No callees found for '{methodName}'.");

        var sb = new StringBuilder();
        sb.AppendLine($"Callees of methods matching '{methodName}':");
        foreach (var (method, callees) in matches)
        {
            sb.AppendLine($"\n  {method}:");
            foreach (var callee in callees)
                sb.AppendLine($"    -> {callee}");
        }
        return Task.FromResult(sb.ToString());
    }

    public Task<string> GetImplementorsAsync(string interfaceName, CancellationToken ct = default)
    {
        if (!State.TryGetValue("inheritance-tree", out var entry))
            return Task.FromResult("Workspace not loaded — inheritance tree not available. Set a workspace and wait for indexing.");

        var tree = JsonSerializer.Deserialize<Dictionary<string, InheritanceInfo>>(entry.Value.ToString()!);
        if (tree is null)
            return Task.FromResult("Failed to deserialize inheritance tree.");

        var matches = tree
            .Where(kvp => kvp.Key.Contains(interfaceName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return Task.FromResult($"No type found matching '{interfaceName}'.");

        var sb = new StringBuilder();
        sb.AppendLine($"Implementors/derived types for '{interfaceName}':");
        foreach (var (typeName, info) in matches)
        {
            if (info.DerivedTypes.Count > 0)
            {
                sb.AppendLine($"\n  {typeName}:");
                foreach (var derived in info.DerivedTypes)
                    sb.AppendLine($"    - {derived}");
            }
            else
            {
                sb.AppendLine($"\n  {typeName}: (no known implementors)");
            }
        }
        return Task.FromResult(sb.ToString());
    }

    public Task<string> GetBaseTypesAsync(string className, CancellationToken ct = default)
    {
        if (!State.TryGetValue("inheritance-tree", out var entry))
            return Task.FromResult("Workspace not loaded — inheritance tree not available. Set a workspace and wait for indexing.");

        var tree = JsonSerializer.Deserialize<Dictionary<string, InheritanceInfo>>(entry.Value.ToString()!);
        if (tree is null)
            return Task.FromResult("Failed to deserialize inheritance tree.");

        var match = tree.FirstOrDefault(kvp =>
            kvp.Key.Contains(className, StringComparison.OrdinalIgnoreCase));

        if (match.Key is null)
            return Task.FromResult($"No type found matching '{className}'.");

        var sb = new StringBuilder();
        sb.AppendLine($"Base type chain for '{match.Key}':");

        var current = match.Key;
        var visited = new HashSet<string>();
        while (current is not null && visited.Add(current))
        {
            if (tree.TryGetValue(current, out var info))
            {
                if (info.Interfaces.Count > 0)
                    sb.AppendLine($"  {current} implements: {string.Join(", ", info.Interfaces)}");
                else
                    sb.AppendLine($"  {current}");

                current = info.BaseType;
            }
            else
            {
                sb.AppendLine($"  {current} (external)");
                break;
            }
        }

        return Task.FromResult(sb.ToString());
    }

    public Task<string> GetOverridesAsync(string methodName, CancellationToken ct = default)
    {
        var hasCallGraph = State.TryGetValue("call-graph", out var callGraphEntry);
        var hasInheritance = State.TryGetValue("inheritance-tree", out var inheritanceEntry);

        if (!hasCallGraph && !hasInheritance)
            return Task.FromResult("Workspace not loaded — call graph and inheritance tree not available. Set a workspace and wait for indexing.");

        var sb = new StringBuilder();
        sb.AppendLine($"Override analysis for '{methodName}':");

        if (hasInheritance)
        {
            var tree = JsonSerializer.Deserialize<Dictionary<string, InheritanceInfo>>(inheritanceEntry!.Value.ToString()!);
            if (tree is not null)
            {
                // find types that have the method in their hierarchy
                var typesWithMethod = new List<string>();
                if (hasCallGraph)
                {
                    var callGraph = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(callGraphEntry!.Value.ToString()!);
                    if (callGraph is not null)
                    {
                        typesWithMethod = callGraph.Keys
                            .Where(k => k.Contains(methodName, StringComparison.OrdinalIgnoreCase))
                            .Select(k =>
                            {
                                var lastDot = k.LastIndexOf('.');
                                return lastDot > 0 ? k[..lastDot] : k;
                            })
                            .Distinct()
                            .ToList();
                    }
                }

                if (typesWithMethod.Count > 0)
                {
                    sb.AppendLine("\n  Types implementing this method:");
                    foreach (var typeName in typesWithMethod)
                    {
                        sb.AppendLine($"    - {typeName}");
                        if (tree.TryGetValue(typeName, out var info) && info.BaseType is not null)
                            sb.AppendLine($"      (base: {info.BaseType})");
                    }
                }
                else
                {
                    sb.AppendLine("  No override information found for this method.");
                }
            }
        }

        return Task.FromResult(sb.ToString());
    }

    public Task<string> GetWorkspaceStatusAsync(CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return Task.FromResult("No workspace set. Workspace is not loaded.");

        if (_workspaceManager is not { IsReady: true })
        {
            var hasCache = State.ContainsKey("call-graph");
            return Task.FromResult(hasCache
                ? $"Workspace: {workspace}\nSolution not loaded (cached data from previous indexing available)."
                : $"Workspace: {workspace}\nSolution not loaded.");
        }

        var projectCount = _workspaceManager.GetProjectNames().Count();
        var hasInheritance = State.TryGetValue("inheritance-tree", out var inheritanceEntry);
        var typeCount = 0;
        if (hasInheritance)
        {
            var tree = JsonSerializer.Deserialize<Dictionary<string, InheritanceInfo>>(inheritanceEntry!.Value.ToString()!);
            typeCount = tree?.Count ?? 0;
        }

        var hasCallGraph = State.TryGetValue("call-graph", out var callGraphEntry);
        var methodCount = 0;
        if (hasCallGraph)
        {
            var graph = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(callGraphEntry!.Value.ToString()!);
            methodCount = graph?.Count ?? 0;
        }

        return Task.FromResult(
            $"Workspace: {workspace}\n" +
            $"Solution loaded: yes\n" +
            $"Projects: {projectCount}\n" +
            $"Types indexed: {typeCount}\n" +
            $"Methods in call graph: {methodCount}");
    }

    public async Task<string> ImplementInterfaceAsync(string filePath, string className, string interfaceName, CancellationToken ct = default)
    {
        var workspace = GetWorkspacePath();
        if (workspace is null)
            return "No workspace set.";

        if (_workspaceManager is not { IsReady: true })
            return "Workspace not loaded — load workspace first for ImplementInterface.";

        var resolvedPath = Path.IsPathRooted(filePath)
            ? Path.GetFullPath(filePath)
            : Path.GetFullPath(Path.Combine(workspace, filePath));

        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var document = _workspaceManager.Solution?.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, resolvedPath, StringComparison.OrdinalIgnoreCase));

        if (document is null)
            return $"File not found in loaded solution: {resolvedPath}";

        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel is null)
            return "Could not get semantic model for file.";

        var syntaxRoot = await document.GetSyntaxRootAsync(ct);
        if (syntaxRoot is not CompilationUnitSyntax root)
            return "Could not get syntax root for file.";

        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);

        if (classDecl is null)
            return $"Class '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct) as INamedTypeSymbol;
        if (classSymbol is null)
            return $"Could not resolve symbol for class '{className}'";

        var interfaceSymbol = classSymbol.AllInterfaces
            .FirstOrDefault(i => i.Name == interfaceName || i.ToDisplayString().Contains(interfaceName));

        if (interfaceSymbol is null)
        {
            // try to find the interface in the compilation
            var compilation = semanticModel.Compilation;
            interfaceSymbol = compilation.GetSymbolsWithName(
                    n => n == interfaceName || n.Contains(interfaceName),
                    SymbolFilter.Type, ct)
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => t.TypeKind == TypeKind.Interface);
        }

        if (interfaceSymbol is null)
            return $"Interface '{interfaceName}' not found in compilation. Make sure the interface is in scope.";

        var implementedMembers = classSymbol.GetMembers()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unimplemented = interfaceSymbol.GetMembers()
            .Where(m => m is IMethodSymbol or IPropertySymbol)
            .Where(m => !implementedMembers.Contains(m.Name))
            .ToList();

        if (unimplemented.Count == 0)
            return $"Class '{className}' already implements all members of '{interfaceName}'.";

        var stubs = new List<MemberDeclarationSyntax>();
        foreach (var member in unimplemented)
        {
            switch (member)
            {
                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    {
                        var returnType = SyntaxFactory.ParseTypeName(method.ReturnType.ToDisplayString());
                        var parameters = method.Parameters.Select(p =>
                            SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name))
                                .WithType(SyntaxFactory.ParseTypeName(p.Type.ToDisplayString())));

                        var isAsync = method.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal);
                        var bodyStatement = method.ReturnsVoid || method.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task"
                            ? SyntaxFactory.ParseStatement("throw new System.NotImplementedException();")
                            : SyntaxFactory.ParseStatement("throw new System.NotImplementedException();");

                        var methodDecl = SyntaxFactory.MethodDeclaration(returnType, method.Name)
                            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters)))
                            .WithBody(SyntaxFactory.Block(bodyStatement));

                        if (isAsync)
                            methodDecl = methodDecl.AddModifiers(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));

                        stubs.Add(methodDecl);
                        break;
                    }
                case IPropertySymbol property:
                    {
                        var propType = SyntaxFactory.ParseTypeName(property.Type.ToDisplayString());
                        var accessors = new List<AccessorDeclarationSyntax>();

                        if (!property.IsWriteOnly)
                            accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement("throw new System.NotImplementedException();"))));

                        if (!property.IsReadOnly)
                            accessors.Add(SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement("throw new System.NotImplementedException();"))));

                        var propDecl = SyntaxFactory.PropertyDeclaration(propType, property.Name)
                            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));

                        stubs.Add(propDecl);
                        break;
                    }
            }
        }

        var updatedClass = classDecl.AddMembers([.. stubs]);
        var updatedRoot = root.ReplaceNode(classDecl, updatedClass).NormalizeWhitespace();

        using var adhocWorkspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
#pragma warning disable RS0030
        var formatted = Microsoft.CodeAnalysis.Formatting.Formatter.Format(updatedRoot, adhocWorkspace);
#pragma warning restore RS0030

        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString(), ct);
        return $"Added {stubs.Count} stub(s) for interface '{interfaceName}' to class '{className}' in {Path.GetFileName(resolvedPath)}";
    }

    public async Task OnStreamEventAsync(CodeChangedMessage evt, StreamSequenceToken? token)
    {
        _ = LoadWorkspaceInBackground(AgentCancellation);
        await Task.CompletedTask;
    }

    public async Task<MessageReceipt> ReceiveAsync(TestResultMessage message, CancellationToken ct = default)
    {
        var eventName = message.Failed == 0 ? "tests.passed" : "tests.failed";
        State[$"test-result-{DateTimeOffset.UtcNow.Ticks}"] = new StateEntry(eventName, $"{message.Passed}/{message.Total} passed");
        await WriteStateAsync(ct);
        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    public Task<bool> CanReceiveAsync(CancellationToken ct = default) => Task.FromResult(true);

    private async Task LoadWorkspaceInBackground(CancellationToken ct)
    {
        try
        {
            var workspace = GetWorkspacePath();
            if (workspace is null) return;

            var solutionPath = FindSolution(workspace);
            if (solutionPath is null) return;

            _workspaceManager = new SolutionWorkspaceManager();
            await _workspaceManager.LoadSolutionAsync(solutionPath, ct);

            var compilations = new List<Compilation>();
            foreach (var projectName in _workspaceManager.GetProjectNames())
            {
                var compilation = await _workspaceManager.GetCompilationAsync(projectName, ct);
                if (compilation is not null)
                    compilations.Add(compilation);
            }

            var callGraph = CallGraphBuilder.Build(compilations);
            var reverseCallGraph = CallGraphBuilder.BuildReverseGraph(callGraph);
            var inheritanceTree = InheritanceTreeBuilder.Build(compilations);

            State["call-graph"] = new StateEntry("call-graph", JsonSerializer.Serialize(callGraph));
            State["reverse-call-graph"] = new StateEntry("reverse-call-graph", JsonSerializer.Serialize(reverseCallGraph));
            State["inheritance-tree"] = new StateEntry("inheritance-tree", JsonSerializer.Serialize(inheritanceTree));
            await WriteStateAsync(ct);

            await PublishAsync("workspace.reindexed", new Dictionary<string, string>
            {
                ["ProjectCount"] = _workspaceManager.GetProjectNames().Count().ToString(),
                ["TypeCount"] = inheritanceTree.Count.ToString()
            }, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* workspace loading is best-effort */ }
    }

    private static string? FindSolution(string workspace)
    {
        var dir = workspace;
        while (dir is not null)
        {
            var slnx = Directory.GetFiles(dir, "*.slnx");
            if (slnx.Length > 0) return slnx[0];
            var sln = Directory.GetFiles(dir, "*.sln");
            if (sln.Length > 0) return sln[0];
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string FormatTypeMap(string workspace, List<TypeEntry> types, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Type map for {workspace} ({source}, {types.Count} types):");
        foreach (var group in types.GroupBy(t => t.Namespace))
        {
            sb.AppendLine($"\n  {(string.IsNullOrEmpty(group.Key) ? "(global)" : group.Key)}:");
            foreach (var t in group)
                sb.AppendLine($"    {t.Kind} {t.Name} -- {t.Methods.Length} methods, {t.Properties.Length} properties");
        }
        return sb.ToString();
    }

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