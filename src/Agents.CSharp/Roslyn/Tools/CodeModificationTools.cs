using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.ComponentModel;

namespace IAW.Agents.Coding.Tools;

public class CodeModificationTools(Func<string> getWorkspacePath)
{
    private string WorkspacePath => getWorkspacePath();

    [Description("Create a new C# file with namespace and type declaration (class, interface, record, or struct).")]
    public async Task<string> CreateFileAsync(
        [Description("Full path for the new file")] string filePath,
        [Description("Namespace for the type")] string namespaceName,
        [Description("Name of the type to create")] string typeName,
        [Description("Kind of type: class, interface, record, struct")] string typeKind,
        [Description("Comma-separated base types (can be empty)")] string baseTypes)
    {
        var resolvedPath = ResolvePath(filePath);

        var typeDeclaration = CreateTypeDeclaration(typeKind, typeName);

        if (!string.IsNullOrWhiteSpace(baseTypes))
        {
            var bases = baseTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var baseList = SyntaxFactory.BaseList(
                SyntaxFactory.SeparatedList<BaseTypeSyntax>(
                    bases.Select(b => SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(b)))));
            typeDeclaration = WithBaseList(typeDeclaration, baseList);
        }

        var namespaceDeclaration = SyntaxFactory.FileScopedNamespaceDeclaration(
                SyntaxFactory.ParseName(namespaceName))
            .AddMembers(typeDeclaration);

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace();

        var formatted = FormatNode(compilationUnit);
        var code = formatted.ToFullString();

        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(resolvedPath, code);
        return $"Created {typeKind} '{typeName}' in namespace '{namespaceName}' at {resolvedPath}";
    }

    [Description("Add a using directive to an existing C# file. Usings are sorted alphabetically.")]
    public async Task<string> AddUsingAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Namespace to add (e.g. System.Text)")] string namespaceName)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();

        var existingUsings = root.Usings
            .Select(u => u.Name?.ToString())
            .Where(n => n is not null)
            .ToHashSet();

        if (existingUsings.Contains(namespaceName))
            return $"Using '{namespaceName}' is already present in {Path.GetFileName(resolvedPath)}";

        var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName));
        var updatedRoot = root.AddUsings(newUsing);

        var sortedUsings = updatedRoot.Usings
            .OrderBy(u => u.Name?.ToString(), StringComparer.Ordinal)
            .ToArray();

        updatedRoot = updatedRoot.WithUsings(SyntaxFactory.List(sortedUsings));

        var formatted = FormatNode(updatedRoot);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());
        return $"Added using '{namespaceName}' to {Path.GetFileName(resolvedPath)}";
    }

    [Description("Add a method to an existing class, record, or struct in a C# file.")]
    public async Task<string> AddMethodAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the class/record/struct to add the method to")] string className,
        [Description("Method signature, e.g. 'public void DoWork(int x)'")] string signature,
        [Description("Method body statements (without braces)")] string body)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();

        var targetType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);

        if (targetType is null)
            return $"Type '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var methodDeclaration = ParseMethodFromSignatureAndBody(signature, body);
        if (methodDeclaration is null)
            return $"Failed to parse method signature: {signature}";

        var updatedType = targetType.AddMembers(methodDeclaration);
        var updatedRoot = root.ReplaceNode(targetType, updatedType);

        var formatted = FormatNode(updatedRoot);

        var formattedSource = formatted.ToFullString();
        var verificationTree = CSharpSyntaxTree.ParseText(formattedSource);
        var verificationRoot = await verificationTree.GetRootAsync();
        var errors = verificationRoot.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return $"Generated code has syntax errors: {string.Join("; ", errors.Select(e => e.GetMessage()))}";

        await File.WriteAllTextAsync(resolvedPath, formattedSource);
        return $"Added method to '{className}' in {Path.GetFileName(resolvedPath)}";
    }

    [Description("Add an auto-property to an existing class, record, or struct in a C# file.")]
    public async Task<string> AddPropertyAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the class/record/struct to add the property to")] string className,
        [Description("Type of the property (e.g. 'string', 'int')")] string propertyType,
        [Description("Name of the property")] string propertyName)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();

        var targetType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);

        if (targetType is null)
            return $"Type '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var property = SyntaxFactory.PropertyDeclaration(
                SyntaxFactory.ParseTypeName(propertyType), propertyName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

        var updatedType = targetType.AddMembers(property);
        var updatedRoot = root.ReplaceNode(targetType, updatedType);

        var formatted = FormatNode(updatedRoot);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());
        return $"Added property '{propertyType} {propertyName}' to '{className}' in {Path.GetFileName(resolvedPath)}";
    }

    [Description("Remove a member (method, property, or field) from a class, record, or struct in a C# file.")]
    public async Task<string> RemoveMemberAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the class/record/struct")] string className,
        [Description("Name of the member to remove")] string memberName)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();

        var targetType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);

        if (targetType is null)
            return $"Type '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var memberToRemove = targetType.Members.FirstOrDefault(m => m switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text == memberName,
            PropertyDeclarationSyntax prop => prop.Identifier.Text == memberName,
            FieldDeclarationSyntax field => field.Declaration.Variables.Any(v => v.Identifier.Text == memberName),
            _ => false
        });

        if (memberToRemove is null)
            return $"Member '{memberName}' not found in '{className}'";

        var updatedType = targetType.RemoveNode(memberToRemove, SyntaxRemoveOptions.KeepNoTrivia)!;
        var updatedRoot = root.ReplaceNode(targetType, updatedType);

        var formatted = FormatNode(updatedRoot);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());
        return $"Removed member '{memberName}' from '{className}' in {Path.GetFileName(resolvedPath)}";
    }

    [Description("Replace the body of an existing method in a class, record, or struct.")]
    public async Task<string> ModifyMethodAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the class/record/struct")] string className,
        [Description("Name of the method to modify")] string methodName,
        [Description("New method body statements (without surrounding braces)")] string newBody)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();

        var targetType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);

        if (targetType is null)
            return $"Type '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var method = targetType.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);

        if (method is null)
            return $"Method '{methodName}' not found in '{className}'";

        var bodyText = newBody.TrimStart().StartsWith('{') ? newBody : $"{{ {newBody} }}";
        var parsedBody = SyntaxFactory.ParseStatement(bodyText) as BlockSyntax;

        if (parsedBody is null)
            return $"Failed to parse new body for method '{methodName}'";

        var updatedMethod = method.WithBody(parsedBody);
        var updatedRoot = root.ReplaceNode(method, updatedMethod);

        var formatted = FormatNode(updatedRoot);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());
        return $"Modified method '{methodName}' in '{className}' in {Path.GetFileName(resolvedPath)}";
    }

    [Description("Add a parameter to an existing method in a class, record, or struct.")]
    public async Task<string> AddParameterAsync(
        [Description("Path to the C# file")] string filePath,
        [Description("Name of the class/record/struct")] string className,
        [Description("Name of the method to add the parameter to")] string methodName,
        [Description("Type of the parameter (e.g. 'string', 'int')")] string paramType,
        [Description("Name of the parameter")] string paramName)
    {
        var resolvedPath = ResolvePath(filePath);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var source = await File.ReadAllTextAsync(resolvedPath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)await tree.GetRootAsync();

        var targetType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == className);

        if (targetType is null)
            return $"Type '{className}' not found in {Path.GetFileName(resolvedPath)}";

        var method = targetType.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == methodName);

        if (method is null)
            return $"Method '{methodName}' not found in '{className}'";

        var newParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(paramName))
            .WithType(SyntaxFactory.ParseTypeName(paramType + " "));

        var updatedMethod = method.AddParameterListParameters(newParameter);
        var updatedRoot = root.ReplaceNode(method, updatedMethod);

        var formatted = FormatNode(updatedRoot);
        await File.WriteAllTextAsync(resolvedPath, formatted.ToFullString());
        return $"Added parameter '{paramType} {paramName}' to method '{methodName}' in '{className}' in {Path.GetFileName(resolvedPath)}";
    }

    static MethodDeclarationSyntax? ParseMethodFromSignatureAndBody(string signature, string body)
    {
        // wrap the signature and body into a class so Roslyn can parse it as a complete member
        var wrappedCode = $"class _Wrapper_ {{ {signature} {{ {body} }} }}";
        var tree = CSharpSyntaxTree.ParseText(wrappedCode);
        var wrapperRoot = tree.GetRoot();

        var method = wrapperRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        return method;
    }

    static TypeDeclarationSyntax CreateTypeDeclaration(string typeKind, string typeName)
    {
        var identifier = SyntaxFactory.Identifier(typeName);
        var publicModifier = SyntaxFactory.Token(SyntaxKind.PublicKeyword);

        return typeKind.ToLowerInvariant() switch
        {
            "interface" => SyntaxFactory.InterfaceDeclaration(identifier)
                .AddModifiers(publicModifier),
            "record" => SyntaxFactory.RecordDeclaration(SyntaxFactory.Token(SyntaxKind.RecordKeyword), identifier)
                .AddModifiers(publicModifier)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)),
            "struct" => SyntaxFactory.StructDeclaration(identifier)
                .AddModifiers(publicModifier),
            _ => SyntaxFactory.ClassDeclaration(identifier)
                .AddModifiers(publicModifier),
        };
    }

    static TypeDeclarationSyntax WithBaseList(TypeDeclarationSyntax typeDeclaration, BaseListSyntax baseList)
    {
        return typeDeclaration switch
        {
            ClassDeclarationSyntax cls => cls.WithBaseList(baseList),
            InterfaceDeclarationSyntax iface => iface.WithBaseList(baseList),
            RecordDeclarationSyntax rec => rec.WithBaseList(baseList),
            StructDeclarationSyntax str => str.WithBaseList(baseList),
            _ => typeDeclaration
        };
    }

    static SyntaxNode FormatNode(SyntaxNode node)
    {
        using var workspace = new AdhocWorkspace();
#pragma warning disable RS0030 // deprecated API — no SyntaxFormattingOptions overload available in public API
        return Formatter.Format(node, workspace);
#pragma warning restore RS0030
    }

    string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(WorkspacePath, path));
    }
}