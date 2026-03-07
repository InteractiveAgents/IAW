using System.Reflection;
using IAW.Core.Attributes;
using IAW.Core.Communication;

namespace IAW.Core.Registry;

public class AgentRegistrationStartupTask(IGrainFactory grainFactory) : IStartupTask
{
    private static readonly HashSet<Type> ExcludedInterfaces =
    [
        typeof(IAgent), typeof(IDynamicAgent), typeof(IEventDrivenAgent),
        typeof(IStreamingAgent), typeof(ITrackableAgent), typeof(IObservableAgent)
    ];

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
        var caps = type.GetCustomAttributes<CapabilityAttribute>().Select(a => a.Capability).ToArray();
        var pubs = type.GetCustomAttributes<PublishesAttribute>().Select(a => a.EventName)
            .Concat(type.GetInterfaces()
                .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IBroadcaster<>) || i.GetGenericTypeDefinition() == typeof(INotifier<>)))
                .Select(i => i.GetGenericArguments()[0].Name))
            .Distinct().ToArray();
        var subs = type.GetCustomAttributes<SubscribesAttribute>().Select(a => a.EventName)
            .Concat(type.GetInterfaces()
                .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IReceiver<>) || i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>)))
                .Select(i => i.GetGenericArguments()[0].Name))
            .Distinct().ToArray();

        return new AgentRegistration(
            type.Name,
            GetAgentShortName(type.Name),
            "",
            type.IsSubclassOf(typeof(DynamicAgent)) ? AgentKind.Dynamic : AgentKind.Static,
            caps, pubs, subs);
    }

    private static string GetAgentShortName(string typeName)
    {
        var name = typeName;
        if (name.EndsWith("Agent")) name = name[..^5];
        return name;
    }
}
