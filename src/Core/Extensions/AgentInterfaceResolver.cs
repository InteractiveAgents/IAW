using Core.Contracts;
using Core.Registry;
using System.Diagnostics;

namespace Core;

public static class AgentInterfaceResolver
{
    public static IReadOnlyList<Type> DiscoverAgentInterfaces() => ScanInterfaces();

    public static Type? Resolve(string name)
    {
        var interfaces = DiscoverAgentInterfaces();

        // exact match: "IGit"
        var match = interfaces.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        // without I prefix: "Git" or "git"
        match = interfaces.FirstOrDefault(t =>
        {
            var stripped = t.Name.StartsWith('I') && t.Name.Length > 1 && char.IsUpper(t.Name[1])
                ? t.Name[1..]
                : t.Name;
            return string.Equals(stripped, name, StringComparison.OrdinalIgnoreCase);
        });
        if (match is not null) return match;

        // kebab-case: "code-orchestrator" -> "CodeOrchestrator" -> "ICodeOrchestrator"
        var normalized = name.Replace("-", "");
        return interfaces.FirstOrDefault(t =>
        {
            var stripped = t.Name.StartsWith('I') && t.Name.Length > 1 && char.IsUpper(t.Name[1])
                ? t.Name[1..]
                : t.Name;
            return string.Equals(stripped, normalized, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static Type? ResolveByDisplayName(string displayName)
    {
        var interfaces = DiscoverAgentInterfaces();
        return interfaces.FirstOrDefault(t =>
        {
            var (name, _, _) = AgentInterfaceMetadata.ReadFrom(t);
            return string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static IReadOnlyList<Type>? _cachedInterfaces;

    private static IReadOnlyList<Type> ScanInterfaces() =>
        _cachedInterfaces ??= AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (Exception ex)
                {
                    Trace.TraceWarning("AgentInterfaceResolver: failed to scan assembly {0}: {1}", a.FullName, ex.Message);
                    return [];
                }
            })
            .Where(t => t.IsInterface
                        && t != typeof(IAgent)
                        && typeof(IAgent).IsAssignableFrom(t)
                        && !t.IsGenericType)
            .ToList();
}