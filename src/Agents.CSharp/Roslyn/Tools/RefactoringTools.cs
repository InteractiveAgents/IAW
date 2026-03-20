using System.ComponentModel;
using IAW.Agents.CSharp.Roslyn.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;

namespace IAW.Agents.Coding.Tools;

public class RefactoringTools(Func<string> getWorkspacePath, SolutionWorkspaceManager? workspaceManager = null)
{
    private string WorkspacePath => getWorkspacePath();

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
