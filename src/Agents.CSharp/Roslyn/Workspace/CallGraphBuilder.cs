using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IAW.Agents.CSharp.Roslyn.Workspace;

public static class CallGraphBuilder
{
    public static Dictionary<string, List<string>> Build(IEnumerable<Compilation> compilations)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var compilation in compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var methodSymbol = model.GetDeclaredSymbol(method);
                    if (methodSymbol is null) continue;

                    var callerKey = GetFullyQualifiedName(methodSymbol);
                    var callees = new List<string>();

                    foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var symbolInfo = model.GetSymbolInfo(invocation);
                        if (symbolInfo.Symbol is IMethodSymbol targetMethod)
                            callees.Add(GetFullyQualifiedName(targetMethod));
                    }

                    if (callees.Count > 0)
                        graph[callerKey] = callees.Distinct().ToList();
                }
            }
        }

        return graph;
    }

    public static Dictionary<string, List<string>> BuildReverseGraph(Dictionary<string, List<string>> forwardGraph)
    {
        var reverse = new Dictionary<string, List<string>>();
        foreach (var (caller, callees) in forwardGraph)
        {
            foreach (var callee in callees)
            {
                if (!reverse.TryGetValue(callee, out var callers))
                {
                    callers = [];
                    reverse[callee] = callers;
                }
                callers.Add(caller);
            }
        }
        return reverse;
    }

    private static string GetFullyQualifiedName(ISymbol symbol)
    {
        var parts = new List<string>();
        var current = symbol;
        while (current is not null and not INamespaceSymbol { IsGlobalNamespace: true })
        {
            parts.Add(current.Name);
            current = current.ContainingSymbol;
        }
        parts.Reverse();
        return string.Join(".", parts);
    }
}
