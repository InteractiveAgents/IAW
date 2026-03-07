namespace IAW.Core;

public interface IDynamicAgent : IAgent
{
    Task ConfigureAsync(AgentConfiguration config, CancellationToken ct);
}
