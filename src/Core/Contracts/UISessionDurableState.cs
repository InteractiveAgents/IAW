using Core.Contracts.UI;
using Orleans.Journaling;

namespace Core.Contracts;

public sealed class UISessionDurableState(
    IDurableDictionary<string, PendingApproval> pendingApprovals)
{
    public IDurableDictionary<string, PendingApproval> PendingApprovals => pendingApprovals;
}
