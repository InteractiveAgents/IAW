using Core.Contracts.UI;
using Orleans.Journaling;

namespace Core.Contracts;

public sealed class UISessionDurableState(
    IDurableDictionary<string, PendingApproval> pendingApprovals,
    IDurableDictionary<string, WizardState> wizards,
    IDurableDictionary<string, string> pendingFreeText)
{
    public IDurableDictionary<string, PendingApproval> PendingApprovals => pendingApprovals;
    public IDurableDictionary<string, WizardState> Wizards => wizards;
    public IDurableDictionary<string, string> PendingFreeText => pendingFreeText;
}
