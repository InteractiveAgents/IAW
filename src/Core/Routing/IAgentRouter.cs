namespace Core.Routing;

public interface IAgentRouter : IGrainWithStringKey
{
    Task<AgentRouteResult> RouteAsync(string message, CancellationToken ct = default);
    Task RebuildRegistryAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public sealed class AgentRouteResult
{
    [Id(0)] public string AgentId { get; set; } = string.Empty;
    [Id(1)] public float Confidence { get; set; }
    [Id(2)] public bool Escalated { get; set; }
}
