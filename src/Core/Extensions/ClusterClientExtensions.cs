using Core.Contracts;

namespace Core;

public static class ClusterClientExtensions
{
    public static T Get<T>(this IClusterClient client) where T : IAgent
        => client.GetGrain<T>($"{typeof(T).Name}-{Guid.NewGuid().ToString("N")[..8]}");

    public static T Get<T>(this IClusterClient client, string scope) where T : IAgent
        => client.GetGrain<T>($"{scope}/{typeof(T).Name}");
}