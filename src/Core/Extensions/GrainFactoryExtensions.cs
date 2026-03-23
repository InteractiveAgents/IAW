using Core.Contracts;

namespace Core;

public static class GrainFactoryExtensions
{
    public static T Get<T>(this IGrainFactory factory) where T : IAgent
        => factory.GetGrain<T>($"{typeof(T).Name}-{Guid.NewGuid().ToString("N")[..8]}");

    public static T Get<T>(this IGrainFactory factory, string scope) where T : IAgent
        => factory.GetGrain<T>($"{scope}/{typeof(T).Name}");
}