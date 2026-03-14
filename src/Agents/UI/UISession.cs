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

    public async Task<CallbackResult> HandleCallback(string callbackId, string callbackData, CancellationToken ct)
    {
        var parts = callbackData.Split(':', 3);
        if (parts.Length < 3)
            return new CallbackResult(null, null, "Invalid callback");

        var (type, id, action) = (parts[0], parts[1], parts[2]);

        if (type == "ap" && state.PendingApprovals.TryGetValue(id, out var approval))
        {
            state.PendingApprovals.Remove(id);
            return new CallbackResult(
                $"\u2705 {approval.Question} \u2014 {action}", action, null);
        }

        if (type == "wz" && state.Wizards.ContainsKey(id))
        {
            var updatedWizard = await AdvanceWizard(id, action, ct);
            if (updatedWizard.CurrentStep >= updatedWizard.Steps.Count)
                return new CallbackResult(null, null, "Wizard completed");

            var nextStep = updatedWizard.Steps[updatedWizard.CurrentStep];
            return new CallbackResult(nextStep.Prompt, null, null);
        }

        return new CallbackResult(null, null, "Unknown callback");
    }

    public Task<WizardState> StartWizard(string wizardId, WizardStep[] steps, string projectSlug, CancellationToken ct)
    {
        var wizardState = new WizardState
        {
            Id = wizardId,
            ProjectSlug = projectSlug,
            Steps = steps,
            CurrentStep = 0,
            Collected = new Dictionary<string, string>()
        };
        state.Wizards[wizardId] = wizardState;
        return Task.FromResult(wizardState);
    }

    public Task<WizardState> AdvanceWizard(string wizardId, string selection, CancellationToken ct)
    {
        if (!state.Wizards.TryGetValue(wizardId, out var wizard))
            throw new KeyNotFoundException($"Wizard '{wizardId}' not found.");

        var currentStep = wizard.Steps[wizard.CurrentStep];
        var updatedCollected = new Dictionary<string, string>(wizard.Collected)
        {
            [currentStep.Id] = selection
        };

        var nextStepIndex = wizard.CurrentStep + 1;
        var updatedWizard = wizard with
        {
            CurrentStep = nextStepIndex,
            Collected = updatedCollected
        };

        if (nextStepIndex >= wizard.Steps.Count)
        {
            // wizard completed — clean up
            state.Wizards.Remove(wizardId);
            foreach (var key in state.PendingFreeText.Keys.ToList())
            {
                if (state.PendingFreeText[key] == wizardId)
                    state.PendingFreeText.Remove(key);
            }
        }
        else
        {
            var nextStep = wizard.Steps[nextStepIndex];
            if (nextStep.Options.Count == 0)
            {
                // free text step — register pending free text for the wizard's project topic
                state.PendingFreeText[wizard.ProjectSlug] = wizardId;
            }

            state.Wizards[wizardId] = updatedWizard;
        }

        return Task.FromResult(updatedWizard);
    }

    public Task<bool> HasPendingFreeTextInput(string topicId, CancellationToken ct)
    {
        return Task.FromResult(state.PendingFreeText.ContainsKey(topicId));
    }
}
