namespace Core.V3;

public interface IDynamicAgent : IAgent
{
    Task ConfigureAsync(AgentConfiguration config, CancellationToken ct);
}
