using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Journaling;

namespace Core.Grains;

[Reentrant]
[GrainType(IAWConstants.GrainTypes.ApprovalGate)]
public class ApprovalGateGrain(
    [FromKeyedServices("requests")] IDurableDictionary<string, ApprovalRequest> requests,
    [FromKeyedServices("decisions")] IDurableDictionary<string, ApprovalDecision> decisions)
    : DurableGrain, IApprovalGate
{
    private readonly Dictionary<string, TaskCompletionSource<ApprovalDecision>> _waiters = [];

    public async Task RequestAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        requests[request.Id] = request;
        await WriteStateAsync(ct);
    }

    public async Task ResolveAsync(string requestId, ApprovalDecision decision, CancellationToken ct = default)
    {
        decisions[requestId] = decision;
        requests.Remove(requestId);
        await WriteStateAsync(ct);

        if (_waiters.TryGetValue(requestId, out var tcs))
        {
            tcs.TrySetResult(decision);
            _waiters.Remove(requestId);
        }
    }

    public Task<ApprovalDecision?> GetResultAsync(string requestId, CancellationToken ct = default)
    {
        decisions.TryGetValue(requestId, out var decision);
        return Task.FromResult(decision);
    }

    public async Task<ApprovalDecision> AwaitDecisionAsync(string requestId, CancellationToken ct = default)
    {
        if (decisions.TryGetValue(requestId, out var existing))
            return existing;

        var tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[requestId] = tcs;
        await using var registration = ct.Register(() =>
        {
            tcs.TrySetCanceled();
            _waiters.Remove(requestId);
        });
        return await tcs.Task;
    }

    public Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ApprovalRequest>>(requests.Values.ToList());
}
