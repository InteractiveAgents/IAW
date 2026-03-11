using System.Text;
using System.Text.RegularExpressions;
using Core.Communication;
using Core.Contracts;

namespace Core.Orchestration;

public static class InterfaceCatalog
{
    public record CatalogEntry(
        string InterfaceName,
        string GrainId,
        Type InterfaceType,
        IReadOnlyList<string> Produces,
        IReadOnlyList<string> Consumes,
        IReadOnlyList<string> Receives);

    private static readonly Type AgentInterface = typeof(IAgent);
    private static readonly Type DynamicAgentInterface = typeof(IDynamicAgent);
    private static readonly Type StreamProducerDef = typeof(IStreamProducer<>);
    private static readonly Type StreamConsumerDef = typeof(IStreamConsumer<>);
    private static readonly Type ReceiverDef = typeof(IReceiver<>);

    public static IReadOnlyList<CatalogEntry> Discover()
    {
        var agentInterfaces = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsInterface
                && AgentInterface.IsAssignableFrom(t)
                && t != AgentInterface
                && t != DynamicAgentInterface)
            .ToList();

        var concreteAgents = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true } && AgentInterface.IsAssignableFrom(t))
            .ToList();

        return agentInterfaces.Select(iface => BuildEntry(iface, concreteAgents)).ToList();
    }

    public static string ComputeGrainId(Type interfaceType)
    {
        var name = interfaceType.Name;
        if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            name = name[1..];

        // insert '-' between lowercase→uppercase transitions only (not digit boundaries)
        name = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", "-");
        return name.ToLowerInvariant();
    }

    public static string ToPromptString(IReadOnlyList<CatalogEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Agent Catalog");
        sb.AppendLine();

        foreach (var entry in entries.OrderBy(e => e.GrainId))
        {
            sb.Append($"- **{entry.InterfaceName}** (id: `{entry.GrainId}`)");

            var details = new List<string>();
            if (entry.Produces.Count > 0)
                details.Add($"publishes: {string.Join(", ", entry.Produces)}");
            if (entry.Consumes.Count > 0)
                details.Add($"subscribes: {string.Join(", ", entry.Consumes)}");
            if (entry.Receives.Count > 0)
                details.Add($"receives: {string.Join(", ", entry.Receives)}");

            if (details.Count > 0)
                sb.Append($" — {string.Join("; ", details)}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static CatalogEntry BuildEntry(Type iface, List<Type> concreteAgents)
    {
        var implementor = concreteAgents.FirstOrDefault(t => iface.IsAssignableFrom(t));

        // union implementor interfaces (which inherit the grain interface contracts) with
        // the grain interface's own declared generic contracts — covers both declaration sites
        var allInterfaces = (implementor?.GetInterfaces() ?? [])
            .Concat(iface.GetInterfaces())
            .Distinct()
            .ToList();

        var produces = allInterfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == StreamProducerDef)
            .Select(i => i.GetGenericArguments()[0].Name)
            .Distinct().ToList();

        var consumes = allInterfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == StreamConsumerDef)
            .Select(i => i.GetGenericArguments()[0].Name)
            .Distinct().ToList();

        var receives = allInterfaces
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == ReceiverDef)
            .Select(i => i.GetGenericArguments()[0].Name)
            .Distinct().ToList();

        return new CatalogEntry(
            iface.Name,
            ComputeGrainId(iface),
            iface,
            produces,
            consumes,
            receives);
    }
}
