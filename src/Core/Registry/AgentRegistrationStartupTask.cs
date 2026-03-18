using Core.Communication;
using Core.Contracts;
using IAW.Core;

namespace Core.Registry;

public class AgentRegistrationStartupTask(IGrainFactory grainFactory) : IStartupTask
{
    public async Task Execute(CancellationToken ct)
    {
        var registry = grainFactory.GetGrain<IAgentRegistryGrain>("global");
        var agentTypes = DiscoverAgentTypes();

        foreach (var type in agentTypes)
        {
            var registration = BuildRegistration(type);
            await registry.RegisterAsync(registration);
        }
    }

    private static IEnumerable<Type> DiscoverAgentTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(Agent)));

    private static AgentRegistration BuildRegistration(Type type)
    {
        var pubs = type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamProducer<>))
            .Select(i => i.GetGenericArguments()[0].Name)
            .Distinct().ToArray();
        var subs = type.GetInterfaces()
            .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IReceiver<>) || i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>)))
            .Select(i => i.GetGenericArguments()[0].Name)
            .Distinct().ToArray();

        return new AgentRegistration(
            type.Name,
            GetAgentShortName(type.Name),
            "",
            pubs, subs);
    }

    private static string GetAgentShortName(string typeName)
    {
        var name = typeName;
        if (name.EndsWith("Agent")) name = name[..^5];
        return name;
    }
}
