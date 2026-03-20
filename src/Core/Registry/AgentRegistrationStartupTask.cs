using Core.Contracts;

namespace Core.Registry;

public class AgentRegistrationStartupTask(IGrainFactory grainFactory) : IStartupTask
{
    public async Task Execute(CancellationToken ct)
    {
        var registry = grainFactory.GetGrain<IAgentRegistry>("global");

        foreach (var agentType in DiscoverAgentTypes())
        {
            var record = BuildRecord(agentType);
            if (record is not null)
                await registry.RegisterAsync(record, ct);
        }
    }

    static IEnumerable<Type> DiscoverAgentTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true }
                && t.IsSubclassOf(typeof(IAW.Core.Agent)));

    static AgentRecord? BuildRecord(Type agentType)
    {
        var agentInterface = agentType.GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && !i.IsGenericType);

        if (agentInterface is null)
            return null;

        var description = agentType.GetProperty("AgentDescription",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as string ?? "";

        var capabilities = agentType.GetProperty("AgentCapabilities",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as string[] ?? [];

        var agentNamespace = ExtractNamespace(agentType);
        var displayName = StripAgentSuffix(agentType.Name);

        return new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = agentType.Name,
            Namespace = agentNamespace,
            DisplayName = displayName,
            Description = description,
            Capabilities = capabilities,
            InterfaceName = agentInterface.Name
        };
    }

    static string ExtractNamespace(Type type)
    {
        var ns = type.Namespace ?? "unknown";
        var lastDot = ns.LastIndexOf('.');
        return lastDot >= 0 ? ns[(lastDot + 1)..].ToLowerInvariant() : ns.ToLowerInvariant();
    }

    static string StripAgentSuffix(string typeName)
        => typeName.EndsWith("Agent") ? typeName[..^5] : typeName;
}
