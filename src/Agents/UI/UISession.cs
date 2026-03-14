using Core.Contracts;
using Core.Contracts.UI;
using Orleans.Journaling;

namespace IAW.Agents.UI;

[GrainType("ui-session-v1")]
public class UISession(
    [UISessionState] UISessionDurableState state)
    : DurableGrain, IUISession
{
    public Task RegisterApproval(string approvalId, string question, string[] options, string projectSlug, CancellationToken ct)
    {
        state.PendingApprovals[approvalId] = new PendingApproval(
            approvalId, question, options.ToList(), projectSlug, 0, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task<ApprovalResult> ResolveApproval(string approvalId, string decision, CancellationToken ct)
    {
        if (!state.PendingApprovals.TryGetValue(approvalId, out var approval))
            throw new KeyNotFoundException($"Approval '{approvalId}' not found.");

        state.PendingApprovals.Remove(approvalId);
        return Task.FromResult(new ApprovalResult(approvalId, decision, approval.ProjectSlug));
    }

    public Task<CallbackResult> HandleCallback(string callbackId, string callbackData, CancellationToken ct)
    {
        var parts = callbackData.Split(':', 3);
        if (parts.Length < 3)
            return Task.FromResult(new CallbackResult(null, null, "Invalid callback"));

        var (type, id, action) = (parts[0], parts[1], parts[2]);

        if (type == "ap" && state.PendingApprovals.TryGetValue(id, out var approval))
        {
            state.PendingApprovals.Remove(id);
            return Task.FromResult(new CallbackResult(
                $"\u2705 {approval.Question} \u2014 {action}", action, null));
        }

        return Task.FromResult(new CallbackResult(null, null, "Unknown callback"));
    }

    public Task<bool> HasPendingFreeTextInput(string topicId, CancellationToken ct)
    {
        return Task.FromResult(false);
    }
}
