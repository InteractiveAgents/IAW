using Microsoft.CodeAnalysis;

namespace IAW.Agents.CSharp.Roslyn.Workspace;

public record InheritanceInfo(
    string? BaseType,
    List<string> Interfaces,
    List<string> DerivedTypes);

public static class InheritanceTreeBuilder
{
    public static Dictionary<string, InheritanceInfo> Build(IEnumerable<Compilation> compilations)
    {
        var tree = new Dictionary<string, InheritanceInfo>();

        foreach (var compilation in compilations)
        {
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot();

                foreach (var node in root.DescendantNodes())
                {
                    if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol) continue;
                    if (typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Interface or TypeKind.Struct)) continue;

                    var fullName = GetFullName(typeSymbol);
                    if (tree.ContainsKey(fullName)) continue;

                    var baseType = typeSymbol.BaseType is { SpecialType: not SpecialType.System_Object }
                        ? GetFullName(typeSymbol.BaseType)
                        : null;
                    var interfaces = typeSymbol.Interfaces
                        .Select(GetFullName)
                        .ToList();

                    tree[fullName] = new InheritanceInfo(baseType, interfaces, []);
                }
            }
        }

        // reverse index: populate DerivedTypes
        foreach (var (typeName, info) in tree)
        {
            if (info.BaseType is not null && tree.TryGetValue(info.BaseType, out var baseInfo))
                baseInfo.DerivedTypes.Add(typeName);

            foreach (var iface in info.Interfaces)
            {
                if (tree.TryGetValue(iface, out var ifaceInfo))
                    ifaceInfo.DerivedTypes.Add(typeName);
            }
        }

        return tree;
    }

    private static string GetFullName(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.IsGlobalNamespace == true
            ? ""
            : symbol.ContainingNamespace?.ToDisplayString() ?? "";
        return string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";
    }
}