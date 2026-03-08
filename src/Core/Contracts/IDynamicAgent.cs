namespace Core.Contracts;

public interface IDynamicAgent : IAgent
{
    Task ConfigureAsync(AgentConfiguration config, CancellationToken ct);
}
