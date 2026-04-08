using Core.Communication;
using Core.Contracts;
using Core.Registry;

namespace IAW.Core;

public abstract partial class Agent
{
    private CancellationTokenSource _cts = new();
    protected CancellationToken AgentCancellation => _cts.Token;
    protected virtual string DisplayName => GetType().Name;
    public Task<AgentMetadata> GetMetadata(CancellationToken ct = default)
    {
        var type = GetType();
        var agentInterface = type.GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && !i.IsGenericType);
        var description = agentInterface is not null
            ? AgentInterfaceMetadata.ReadFrom(agentInterface).Description
            : DisplayName;

        return Task.FromResult(new AgentMetadata(
            type.Name, DisplayName, description,
            DiscoverPublishedMessageTypes(type), DiscoverReceivedMessageTypes(type)));
    }

    public Task<AgentCapabilities> GetCapabilities(CancellationToken ct = default)
    {
        var type = GetType();

        return Task.FromResult(new AgentCapabilities(
            HasMemory: true,
            HasP2P: HasInterface(type, typeof(IReceiver<>)),
            HasEvents: HasInterface(type, typeof(IStreamConsumer<>)) || HasInterface(type, typeof(IStreamProducer<>)),
            HasTimers: true,
            IsCancellable: true,
            HasTools: GetAllTools().Count > 0));
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
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamProducer<>))
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