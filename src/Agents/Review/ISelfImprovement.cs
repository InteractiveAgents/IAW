using IAW.Core;

namespace IAW.Agents.Review;

public interface ISelfImprovement : IAgent
{
    Task<string[]> GetPendingProposalsAsync(CancellationToken ct = default);
    Task<string> GetMetricsSummaryAsync(CancellationToken ct = default);
    Task TriggerAnalysisAsync(CancellationToken ct = default);
}
