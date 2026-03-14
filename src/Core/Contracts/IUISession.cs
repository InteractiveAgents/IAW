using Core.Contracts.UI;

namespace Core.Contracts;

public interface IUISession : IGrainWithStringKey
{
    Task<CallbackResult> HandleCallback(string callbackId, string callbackData, CancellationToken ct);
    Task RegisterApproval(string approvalId, string question, string[] options, string projectSlug, CancellationToken ct);
    Task<ApprovalResult> ResolveApproval(string approvalId, string decision, CancellationToken ct);
    Task<bool> HasPendingFreeTextInput(string topicId, CancellationToken ct);
    Task<WizardState> StartWizard(string wizardId, WizardStep[] steps, string projectSlug, CancellationToken ct);
    Task<WizardState> AdvanceWizard(string wizardId, string selection, CancellationToken ct);
}
