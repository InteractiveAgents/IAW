namespace Core.Contracts;

[GenerateSerializer]
public record AgentCapabilities(
    [property: Id(0)] bool HasMemory,
    [property: Id(1)] bool HasP2P,
    [property: Id(2)] bool HasEvents,
    [property: Id(3)] bool HasTimers,
    [property: Id(4)] bool IsCancellable,
    [property: Id(5)] bool HasTools);