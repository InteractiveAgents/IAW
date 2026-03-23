using Core.Contracts;

namespace Core.Registry;

public class AgentRegistrationStartupTask(IGrainFactory grainFactory) : IStartupTask
{
    public async Task Execute(CancellationToken ct)
    {
        var registry = grainFactory.GetGrain<IAgentRegistry>("global");

        foreach (var record in DiscoverAndBuildRecords())
            await registry.RegisterAsync(record, ct);
    }

    public static IEnumerable<AgentRecord> DiscoverAndBuildRecords() =>
        DiscoverAgentTypes().Select(BuildRecord).Where(r => r is not null)!;

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

        var meta = AgentInterfaceMetadata.ReadFrom(agentInterface);

        var agentNamespace = ExtractNamespace(agentType);
        var displayName = meta.DisplayName.Length > 0
            ? meta.DisplayName
            : StripAgentSuffix(agentType.Name);

        return new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = agentType.Name,
            Namespace = agentNamespace,
            DisplayName = displayName,
            Description = meta.Description,
            Capabilities = meta.Capabilities,
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