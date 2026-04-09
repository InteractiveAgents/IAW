using IAW.Agents.CSharp.Roslyn.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using System.ComponentModel;

namespace IAW.Agents.Coding.Tools;

public class RefactoringTools(Func<string> getWorkspacePath, SolutionWorkspaceManager? workspaceManager = null)
{
    private string WorkspacePath => getWorkspacePath();

    [Description("Move a type declaration from one file to another, preserving namespace and using directives.")]
    public async Task<string> MoveTypeAsync(
        [Description("Path to the source file containing the type")] string sourceFilePath,
        [Description("Name of the type to move")] string typeName,
        [Description("Path to the target file where the type will be placed")] string targetFilePath)
    {
        var resolvedSource = ResolvePath(sourceFilePath);
        var resolvedTarget = ResolvePath(targetFilePath);

        if (!File.Exists(resolvedSource))
            return $"Source file not found: {resolvedSource}";

        var sourceText = await File.ReadAllTextAsync(resolvedSource);
        var sourceTree = CSharpSyntaxTree.ParseText(sourceText);
        var sourceRoot = (CompilationUnitSyntax)await sourceTree.GetRootAsync();

        var typeToMove = sourceRoot.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == typeName);

        if (typeToMove is null)
            return $"Type '{typeName}' not found in {Path.GetFileName(resolvedSource)}";

        var usings = sourceRoot.Usings;

        var targetMembers = BuildTargetMembers(sourceRoot, typeToMove);
        var targetUnit = SyntaxFactory.CompilationUnit()
            .WithUsings(usings)
            .WithMembers(targetMembers)
            .NormalizeWhitespace();

        var formattedTarget = FormatNode(targetUnit);
        await File.WriteAllTextAsync(resolvedTarget, formattedTarget.ToFullString());

        var updatedSource = sourceRoot.RemoveNode(typeToMove, SyntaxRemoveOptions.KeepNoTrivia)!;
        var formattedSource = FormatNode(updatedSource);
        await File.WriteAllTextAsync(resolvedSource, formattedSource.ToFullString());

        return $"Moved type '{typeName}' from {Path.GetFileName(resolvedSource)} to {Path.GetFileName(resolvedTarget)}";
    }

    static SyntaxList<MemberDeclarationSyntax> BuildTargetMembers(
        CompilationUnitSyntax sourceRoot, TypeDeclarationSyntax typeToMove)
    {
        var fileScopedNs = sourceRoot.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        var blockNs = sourceRoot.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();

        if (fileScopedNs is not null)
        {
            var newNs = SyntaxFactory.FileScopedNamespaceDeclaration(fileScopedNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeToMove.WithoutLeadingTrivia()));
            return SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs);
        }

        if (blockNs is not null)
        {
            var newNs = SyntaxFactory.NamespaceDeclaration(blockNs.Name)
                .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeToMove.WithoutLeadingTrivia()));
            return SyntaxFactory.SingletonList<MemberDeclarationSyntax>(newNs);
        }

        return SyntaxFactory.SingletonList<MemberDeclarationSyntax>(typeToMove.WithoutLeadingTrivia());
    }

    [Description("Inline a local variable by replacing all its usages with the initializer expression and removing the declaration.")]
    public async Task<string> InlineVariableAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the local variable to inline")] string variableName,
        [Description("1-based line number where the variable is declared")] int line)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var sourceText = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = await tree.GetRootAsync();

        var localDeclaration = root.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .FirstOrDefault(ld =>
            {
                var lineSpan = ld.GetLocation().GetLineSpan();
                var declarationLine = lineSpan.StartLinePosition.Line + 1;
                return declarationLine == line &&
                       ld.Declaration.Variables.Any(v => v.Identifier.Text == variableName);
            });

        if (localDeclaration is null)
            return $"Variable '{variableName}' not found on line {line} in {Path.GetFileName(resolvedPath)}";

        var declarator = localDeclaration.Declaration.Variables
            .First(v => v.Identifier.Text == variableName);

        if (declarator.Initializer?.Value is not { } initializerExpression)
            return $"Variable '{variableName}' has no initializer to inline";

        var containingMethod = localDeclaration.Ancestors()
            .FirstOrDefault(a => a is MethodDeclarationSyntax or LocalFunctionStatementSyntax
                or AccessorDeclarationSyntax or ConstructorDeclarationSyntax);

        if (containingMethod is null)
            return $"Could not find containing method for variable '{variableName}'";

        var rewriter = new InlineVariableRewriter(variableName, initializerExpression, localDeclaration);
        var rewrittenRoot = rewriter.Visit(root);

        var formatted = FormatNode(rewrittenRoot);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());

        return $"Inlined variable '{variableName}' — replaced {rewriter.ReplacementCount} usage(s) and removed declaration";
    }

    [Description("Rename a symbol (class, method, property, variable) and all its references within a file or across the solution.")]
    public async Task<string> RenameSymbolAsync(
        [Description("Current name of the symbol to rename")] string symbolName,
        [Description("New name for the symbol")] string newName,
        [Description("Path to the C# file containing the symbol")] string filePath)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        if (workspaceManager is { IsReady: true } && workspaceManager.Solution is { } solution)
            return await RenameWithWorkspaceAsync(solution, symbolName, newName, resolvedPath);

        return await RenameWithRewriterAsync(symbolName, newName, resolvedPath);
    }

    async Task<string> RenameWithWorkspaceAsync(Solution solution, string symbolName, string newName, string filePath)
    {
        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        if (document is null)
            return await RenameWithRewriterAsync(symbolName, newName, filePath);

        var semanticModel = await document.GetSemanticModelAsync();
        if (semanticModel is null)
            return await RenameWithRewriterAsync(symbolName, newName, filePath);

        var root = await document.GetSyntaxRootAsync();
        if (root is null)
            return await RenameWithRewriterAsync(symbolName, newName, filePath);

        var declarationNode = root.DescendantNodes()
            .FirstOrDefault(n => GetDeclaredIdentifier(n) == symbolName);

        if (declarationNode is null)
            return await RenameWithRewriterAsync(symbolName, newName, filePath);

        var symbol = semanticModel.GetDeclaredSymbol(declarationNode);
        if (symbol is null)
            return await RenameWithRewriterAsync(symbolName, newName, filePath);

        var renamedSolution = await Renamer.RenameSymbolAsync(
            solution, symbol, new SymbolRenameOptions(), newName);

        var changedDocIds = renamedSolution.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(pc => pc.GetChangedDocuments())
            .ToList();

        foreach (var docId in changedDocIds)
        {
            var changedDoc = renamedSolution.GetDocument(docId);
            if (changedDoc?.FilePath is null) continue;

            var text = await changedDoc.GetTextAsync();
            await File.WriteAllTextAsync(changedDoc.FilePath, text.ToString());
        }

        return $"Renamed '{symbolName}' to '{newName}' across {changedDocIds.Count} file(s) using workspace";
    }

    async Task<string> RenameWithRewriterAsync(string symbolName, string newName, string filePath)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();

        var hasDeclaration = root.DescendantNodes()
            .Any(n => GetDeclaredIdentifier(n) == symbolName);

        if (!hasDeclaration)
            return $"Symbol '{symbolName}' not found in {Path.GetFileName(filePath)}";

        var rewriter = new SymbolRenamingRewriter(symbolName, newName);
        var rewrittenRoot = rewriter.Visit(root);

        var formatted = FormatNode(rewrittenRoot);
        await File.WriteAllTextAsync(filePath, formatted.ToFullString());

        return $"Renamed '{symbolName}' to '{newName}' in {Path.GetFileName(filePath)} ({rewriter.ReplacementCount} occurrence(s))";
    }

    static string? GetDeclaredIdentifier(SyntaxNode node) => node switch
    {
        TypeDeclarationSyntax t => t.Identifier.Text,
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        VariableDeclaratorSyntax v => v.Identifier.Text,
        ParameterSyntax param => param.Identifier.Text,
        EnumDeclarationSyntax e => e.Identifier.Text,
        EnumMemberDeclarationSyntax em => em.Identifier.Text,
        EventDeclarationSyntax ev => ev.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        LocalFunctionStatementSyntax lf => lf.Identifier.Text,
        _ => null
    };

    static SyntaxNode FormatNode(SyntaxNode node)
    {
        using var workspace = new AdhocWorkspace();
#pragma warning disable RS0030
        return Formatter.Format(node, workspace);
#pragma warning restore RS0030
    }

    string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(WorkspacePath, path));
    }

    [Description("Extract statements from a method into a new method. Uses syntax-based analysis to detect parameters and return values.")]
    public async Task<string> ExtractMethodAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("First line to extract (1-based)")] int startLine,
        [Description("Last line to extract (1-based)")] int endLine,
        [Description("Name for the new method")] string newMethodName)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();

        var sourceLines = source.Split('\n');
        if (startLine < 1 || endLine < startLine || endLine > sourceLines.Length)
            return $"Invalid line range {startLine}-{endLine} (file has {sourceLines.Length} lines)";

        // find statements that overlap the requested line range
        var allStatements = root.DescendantNodes().OfType<StatementSyntax>()
            .Where(s => s is not BlockSyntax)
            .ToList();

        var extractedStatements = allStatements
            .Where(s =>
            {
                var span = s.GetLocation().GetLineSpan();
                var stmtStart = span.StartLinePosition.Line + 1;
                var stmtEnd = span.EndLinePosition.Line + 1;
                return stmtStart >= startLine && stmtEnd <= endLine;
            })
            .ToList();

        if (extractedStatements.Count == 0)
            return $"No statements found in line range {startLine}-{endLine}";

        var containingMethod = extractedStatements[0].Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod is null)
            return "Could not find containing method for the extracted statements";

        var containingClass = containingMethod.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingClass is null)
            return "Could not find containing class";

        // syntax-based variable analysis
        var declaredInRange = new HashSet<string>();
        var referencedInRange = new HashSet<string>();

        foreach (var stmt in extractedStatements)
        {
            foreach (var decl in stmt.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                declaredInRange.Add(decl.Identifier.Text);

            foreach (var id in stmt.DescendantNodes().OfType<IdentifierNameSyntax>())
                referencedInRange.Add(id.Identifier.Text);
        }

        // variables referenced but not declared in range become parameters
        var parameters = referencedInRange.Except(declaredInRange).ToList();

        // check if any variable declared in range is used after the range
        var statementsAfterRange = containingMethod.Body?.Statements
            .Where(s =>
            {
                var span = s.GetLocation().GetLineSpan();
                return span.StartLinePosition.Line + 1 > endLine;
            })
            .ToList() ?? [];

        var usedAfterRange = new HashSet<string>();
        foreach (var stmt in statementsAfterRange)
            foreach (var id in stmt.DescendantNodes().OfType<IdentifierNameSyntax>())
                if (declaredInRange.Contains(id.Identifier.Text))
                    usedAfterRange.Add(id.Identifier.Text);

        var returnVariable = usedAfterRange.Count == 1 ? usedAfterRange.First() : null;

        // find the type of the return variable from its declaration
        TypeSyntax returnType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));
        if (returnVariable is not null)
        {
            var returnDeclarator = extractedStatements
                .SelectMany(s => s.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                .FirstOrDefault(v => v.Identifier.Text == returnVariable);

            if (returnDeclarator?.Parent is VariableDeclarationSyntax varDecl)
            {
                if (varDecl.Type.IsVar)
                    returnType = SyntaxFactory.IdentifierName("var");
                else
                    returnType = varDecl.Type;
            }
        }

        // use "var" return type as "object" for method signature since var is not valid as return type
        var methodReturnType = returnType is IdentifierNameSyntax { Identifier.Text: "var" }
            ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)) as TypeSyntax
            : returnType;

        // build parameter list for new method — use "object" type for syntax-only analysis
        var parameterSyntaxList = parameters
            .Where(p => !IsKnownTypeName(p, root))
            .Select(p =>
                SyntaxFactory.Parameter(SyntaxFactory.Identifier(p))
                    .WithType(FindVariableType(p, containingMethod) ??
                        SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword))))
            .ToList();

        var extractedBody = SyntaxFactory.Block(
            SyntaxFactory.List(extractedStatements.Select(s => s.WithoutLeadingTrivia().WithoutTrailingTrivia())));

        if (returnVariable is not null)
        {
            extractedBody = extractedBody.AddStatements(
                SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(returnVariable)));
        }

        var newMethod = SyntaxFactory.MethodDeclaration(methodReturnType, newMethodName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameterSyntaxList)))
            .WithBody(extractedBody)
            .NormalizeWhitespace();

        // build call expression
        var arguments = parameterSyntaxList.Select(p =>
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(p.Identifier))).ToList();

        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(newMethodName),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));

        StatementSyntax callStatement;
        if (returnVariable is not null)
        {
            callStatement = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(returnVariable)
                            .WithInitializer(SyntaxFactory.EqualsValueClause(invocation)))));
        }
        else
        {
            callStatement = SyntaxFactory.ExpressionStatement(invocation);
        }

        // replace extracted statements in the containing method
        var newBody = containingMethod.Body!;
        var firstExtracted = extractedStatements[0];
        var lastExtracted = extractedStatements[^1];

        var bodyStatements = newBody.Statements.ToList();
        var firstIdx = bodyStatements.IndexOf(firstExtracted);
        var lastIdx = bodyStatements.IndexOf(lastExtracted);

        if (firstIdx < 0 || lastIdx < 0)
            return "Could not locate extracted statements in method body";

        var newStatements = new List<StatementSyntax>();
        for (var i = 0; i < bodyStatements.Count; i++)
        {
            if (i == firstIdx)
                newStatements.Add(callStatement);
            else if (i < firstIdx || i > lastIdx)
                newStatements.Add(bodyStatements[i]);
        }

        var updatedMethod = containingMethod.WithBody(
            SyntaxFactory.Block(SyntaxFactory.List(newStatements)));

        // insert new method after the containing method in the class
        var updatedClass = containingClass.ReplaceNode(containingMethod, updatedMethod);
        var insertionIndex = updatedClass.Members.IndexOf(
            updatedClass.Members.OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == containingMethod.Identifier.Text)) + 1;

        updatedClass = updatedClass.WithMembers(
            updatedClass.Members.Insert(insertionIndex, newMethod));

        root = root.ReplaceNode(containingClass, updatedClass);
        var formatted = FormatNode(root);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());

        return $"Extracted {extractedStatements.Count} statement(s) into '{newMethodName}' in {Path.GetFileName(resolvedPath)}";
    }

    [Description("Change the parameter list of a method in a class.")]
    public async Task<string> ChangeSignatureAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the class containing the method")] string className,
        [Description("Name of the method to change")] string methodName,
        [Description("New parameter list (e.g. 'string name, int age')")] string newParameters)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();

        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className);

        if (classDecl is null)
            return $"Class '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var methodDecl = classDecl.Members.OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);

        if (methodDecl is null)
            return $"Method '{methodName}' not found in class '{className}'";

        // parse the new parameter string into a parameter list
        var newParamList = ParseParameterList(newParameters);
        var updatedMethod = methodDecl.WithParameterList(newParamList);

        root = root.ReplaceNode(methodDecl, updatedMethod);
        var formatted = FormatNode(root);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());

        return $"Changed signature of '{className}.{methodName}' to ({newParameters}) in {Path.GetFileName(resolvedPath)}";
    }

    static ParameterListSyntax ParseParameterList(string parametersText)
    {
        if (string.IsNullOrWhiteSpace(parametersText))
            return SyntaxFactory.ParameterList();

        // parse a dummy method to extract the parameter list
        var dummyCode = $"class _C {{ void _M({parametersText}) {{ }} }}";
        var dummyTree = CSharpSyntaxTree.ParseText(dummyCode);
        var dummyRoot = dummyTree.GetRoot();
        var dummyMethod = dummyRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        return dummyMethod.ParameterList;
    }

    static bool IsKnownTypeName(string name, SyntaxNode root)
    {
        return root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .Any(t => t.Identifier.Text == name);
    }

    static TypeSyntax? FindVariableType(string variableName, MethodDeclarationSyntax method)
    {
        // check local variable declarations
        var declarator = method.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.Text == variableName);

        if (declarator?.Parent is VariableDeclarationSyntax varDecl && !varDecl.Type.IsVar)
            return varDecl.Type;

        // check parameters
        var param = method.ParameterList.Parameters
            .FirstOrDefault(p => p.Identifier.Text == variableName);

        return param?.Type;
    }

    sealed class InlineVariableRewriter(
        string variableName,
        ExpressionSyntax initializerExpression,
        LocalDeclarationStatementSyntax declarationToRemove) : CSharpSyntaxRewriter
    {
        public int ReplacementCount { get; private set; }
        bool _declarationRemoved;

        public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            if (!_declarationRemoved && node.IsEquivalentTo(declarationToRemove))
            {
                _declarationRemoved = true;
                return null!;
            }
            return base.VisitLocalDeclarationStatement(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text == variableName)
            {
                // skip the identifier inside the declaration itself
                if (node.Ancestors().Any(a => a is LocalDeclarationStatementSyntax ld && ld.IsEquivalentTo(declarationToRemove)))
                    return base.VisitIdentifierName(node);

                ReplacementCount++;
                return initializerExpression.WithTriviaFrom(node);
            }
            return base.VisitIdentifierName(node);
        }
    }

    sealed class SymbolRenamingRewriter(string oldName, string newName) : CSharpSyntaxRewriter
    {
        public int ReplacementCount { get; private set; }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            node = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        {
            node = (StructDeclarationSyntax)base.VisitStructDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            node = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            node = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            node = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            node = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            node = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitParameter(ParameterSyntax node)
        {
            node = (ParameterSyntax)base.VisitParameter(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            node = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            node = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            node = (GenericNameSyntax)base.VisitGenericName(node)!;
            if (node.Identifier.Text == oldName)
            {
                ReplacementCount++;
                return node.WithIdentifier(SyntaxFactory.Identifier(newName)
                    .WithTriviaFrom(node.Identifier));
            }
            return node;
        }
    }
}