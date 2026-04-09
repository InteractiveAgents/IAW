namespace Core.Contracts;

public interface IApprovalGate : IGrainWithStringKey
{
    Task RequestAsync(ApprovalRequest request, CancellationToken ct = default);
    Task ResolveAsync(string requestId, ApprovalDecision decision, CancellationToken ct = default);
    Task<ApprovalDecision?> GetResultAsync(string requestId, CancellationToken ct = default);
    Task<ApprovalDecision> AwaitDecisionAsync(string requestId, CancellationToken ct = default);
    Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default);
}
