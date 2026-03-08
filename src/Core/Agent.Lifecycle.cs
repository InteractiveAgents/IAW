using System.Reflection;
using IAW.Core.Attributes;
using IAW.Core.Communication;
using IAW.Core.Observability;

namespace IAW.Core;

public abstract partial class Agent
{
    private CancellationTokenSource _cts = new();
    protected CancellationToken AgentCancellation => _cts.Token;
    protected virtual string DisplayName => GetType().Name;
    protected virtual AgentKind AgentKindValue => AgentKind.Static;

    public Task<AgentMetadata> GetMetadata(CancellationToken ct = default)
    {
        var type = GetType();
        var publishedFromInterfaces = DiscoverPublishedMessageTypes(type);
        var publishedFromAttributes = type.GetCustomAttributes<PublishesAttribute>().Select(a => a.EventName);
        var publishes = publishedFromInterfaces.Concat(publishedFromAttributes).Distinct().ToArray();

        var subscribedFromInterfaces = DiscoverReceivedMessageTypes(type);
        var subscribedFromAttributes = type.GetCustomAttributes<SubscribesAttribute>().Select(a => a.EventName);
        var subscribes = subscribedFromInterfaces.Concat(subscribedFromAttributes).Distinct().ToArray();

        var capabilities = type.GetCustomAttributes<CapabilityAttribute>().Select(a => a.Capability).ToArray();

        return Task.FromResult(new AgentMetadata(
            type.Name, DisplayName, Instructions, AgentKindValue,
            capabilities, publishes, subscribes));
    }

    public Task<AgentCapabilities> GetCapabilities(CancellationToken ct = default)
    {
        var type = GetType();
        var attributeCaps = type.GetCustomAttributes<CapabilityAttribute>()
            .Select(a => a.Capability).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(new AgentCapabilities(
            HasMemory: true,
            HasP2P: HasInterface(type, typeof(IReceiver<>)) || attributeCaps.Contains("P2P"),
            HasEvents: HasInterface(type, typeof(IStreamConsumer<>)) || HasInterface(type, typeof(IStreamProducer<>)) || attributeCaps.Contains("Events"),
            HasTimers: true,
            IsCancellable: true,
            IsMultiState: attributeCaps.Contains("Multi-state"),
            HasTools: GetAllTools().Count > 0,
            IsSecure: attributeCaps.Contains("Secure")));
    }

    public Task Cancel(CancellationToken ct)
    {
        var old = _cts;
        _cts = new CancellationTokenSource();
        old.Cancel();
        old.Dispose();
        return Task.CompletedTask;
    }

    private static string[] DiscoverPublishedMessageTypes(Type type) =>
    [
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBroadcaster<>))
            .Select(i => i.GetGenericArguments()[0].Name),
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotifier<>))
            .Select(i => i.GetGenericArguments()[0].Name),
    ];

    private static string[] DiscoverReceivedMessageTypes(Type type) =>
    [
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReceiver<>))
            .Select(i => i.GetGenericArguments()[0].Name),
        .. type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>))
            .Select(i => i.GetGenericArguments()[0].Name),
    ];

    private static bool HasInterface(Type type, Type openGenericInterface)
        => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);
}
