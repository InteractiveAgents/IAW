using Core;
using Core.Contracts;
using Core.Contracts.UI;
using Orleans.Journaling;

namespace IAW.Agents.UI;

[GrainType(IAWConstants.GrainTypes.UISession)]
public class UISession(
    [UISessionState] UISessionDurableState state)
    : DurableGrain, IUISession, IRemindable
{
    static readonly TimeSpan PaginatorTimeout = TimeSpan.FromMinutes(30);
    static readonly TimeSpan MenuTimeout = TimeSpan.FromMinutes(10);
    static readonly TimeSpan WizardFormTimeout = TimeSpan.FromMinutes(60);

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await this.RegisterOrUpdateReminder("widget-cleanup", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != "widget-cleanup") return;

        var now = DateTimeOffset.UtcNow;

        foreach (var key in state.Paginators.Keys.ToList())
            if (now - state.Paginators[key].CreatedAt > PaginatorTimeout)
                state.Paginators.Remove(key);

        foreach (var key in state.Menus.Keys.ToList())
            if (now - state.Menus[key].CreatedAt > MenuTimeout)
                state.Menus.Remove(key);

        foreach (var key in state.Wizards.Keys.ToList())
            if (now - state.Wizards[key].CreatedAt > WizardFormTimeout)
            {
                state.Wizards.Remove(key);
                foreach (var ftKey in state.PendingFreeText.Keys.ToList())
                    if (state.PendingFreeText[ftKey] == key)
                        state.PendingFreeText.Remove(ftKey);
            }

        foreach (var key in state.Forms.Keys.ToList())
            if (now - state.Forms[key].CreatedAt > WizardFormTimeout)
            {
                state.Forms.Remove(key);
                foreach (var ftKey in state.PendingFreeText.Keys.ToList())
                    if (state.PendingFreeText[ftKey] == key)
                        state.PendingFreeText.Remove(ftKey);
            }

    }
    public Task RegisterApproval(string approvalId, string question, string[] options, string projectSlug, CancellationToken ct)
    {
        state.PendingApprovals[approvalId] = new PendingApproval(
            approvalId, question, options, projectSlug, 0, DateTimeOffset.UtcNow);
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
            var buttons = nextStep.Options.Count > 0 ? nextStep.Options.ToList() : null;
            return new CallbackResult(nextStep.Prompt, null, null, buttons);
        }

        if (type == "pg" && state.Paginators.ContainsKey(id))
        {
            var updated = await NavigatePaginator(id, action, ct);
            return RenderPaginatorResult(updated);
        }

        if (type == "mn" && state.Menus.ContainsKey(id))
        {
            var updated = await NavigateMenu(id, action, ct);
            return RenderMenuResult(updated);
        }

        if (type == "fm" && state.Forms.ContainsKey(id))
        {
            var form = state.Forms[id];
            var currentField = form.Fields[form.CurrentField];

            if (currentField.Type == FormFieldType.MultiChoice && action != "__done__")
            {
                var toggled = ToggleMultiChoiceSelection(form, action);
                state.Forms[id] = toggled;
                return RenderMultiChoiceResult(toggled);
            }

            var advanced = await AdvanceForm(id, action, ct);
            if (advanced.CurrentField >= advanced.Fields.Count)
                return new CallbackResult(null, null, "Form completed");

            var nextField = advanced.Fields[advanced.CurrentField];
            return RenderFormFieldResult(advanced, nextField);
        }

        return new CallbackResult(null, null, "Unknown callback");
    }

    public Task<WizardState> StartWizard(string wizardId, WizardStep[] steps, string projectSlug, CancellationToken ct)
    {
        if (state.Wizards.TryGetValue(wizardId, out var existing))
            return Task.FromResult(existing);

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
                state.PendingFreeText[wizard.ProjectSlug] = wizardId;
            }
            else if (state.PendingFreeText.ContainsKey(wizard.ProjectSlug))
            {
                state.PendingFreeText.Remove(wizard.ProjectSlug);
            }

            state.Wizards[wizardId] = updatedWizard;
        }

        return Task.FromResult(updatedWizard);
    }

    public Task<bool> HasPendingFreeTextInput(string topicId, CancellationToken ct)
    {
        return Task.FromResult(state.PendingFreeText.ContainsKey(topicId));
    }

    public Task<PaginatorState> StartPaginator(string paginatorId, string[] items, int pageSize, string projectSlug, CancellationToken ct)
    {
        if (pageSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be positive.");

        if (state.Paginators.TryGetValue(paginatorId, out var existing))
            return Task.FromResult(existing);

        var paginatorState = new PaginatorState
        {
            Id = paginatorId,
            ProjectSlug = projectSlug,
            Items = items,
            PageSize = pageSize,
            CurrentPage = 0
        };
        state.Paginators[paginatorId] = paginatorState;
        return Task.FromResult(paginatorState);
    }

    public Task<PaginatorState> NavigatePaginator(string paginatorId, string direction, CancellationToken ct)
    {
        if (!state.Paginators.TryGetValue(paginatorId, out var paginator))
            throw new KeyNotFoundException($"Paginator '{paginatorId}' not found.");

        var maxPage = Math.Max(0, (int)Math.Ceiling((double)paginator.Items.Count / paginator.PageSize) - 1);

        var newPage = direction switch
        {
            "next" => Math.Min(paginator.CurrentPage + 1, maxPage),
            "prev" => Math.Max(paginator.CurrentPage - 1, 0),
            _ => (int?)null
        };

        if (newPage is null || newPage == paginator.CurrentPage)
            return Task.FromResult(paginator);

        var updated = paginator with { CurrentPage = newPage.Value };
        state.Paginators[paginatorId] = updated;
        return Task.FromResult(updated);
    }

    public Task<MenuState> StartMenu(string menuId, MenuNode root, string projectSlug, CancellationToken ct)
    {
        if (state.Menus.TryGetValue(menuId, out var existing))
            return Task.FromResult(existing);

        ValidateMenuLabels(root);

        var menuState = new MenuState
        {
            Id = menuId,
            ProjectSlug = projectSlug,
            Root = root,
            BreadCrumb = new List<string>()
        };
        state.Menus[menuId] = menuState;
        return Task.FromResult(menuState);
    }

    public Task<MenuState> NavigateMenu(string menuId, string action, CancellationToken ct)
    {
        if (!state.Menus.TryGetValue(menuId, out var menu))
            throw new KeyNotFoundException($"Menu '{menuId}' not found.");

        if (action == "__back__")
        {
            if (menu.BreadCrumb.Count == 0)
                return Task.FromResult(menu);

            var shortenedCrumb = menu.BreadCrumb.Take(menu.BreadCrumb.Count - 1).ToList();
            var updated = menu with { BreadCrumb = shortenedCrumb };
            state.Menus[menuId] = updated;
            return Task.FromResult(updated);
        }

        var currentNode = ResolveMenuNode(menu.Root, menu.BreadCrumb);
        var child = currentNode?.Children?.FirstOrDefault(c => c.Label == action);

        if (child is null)
            return Task.FromResult(menu);

        var newCrumb = menu.BreadCrumb.Concat(new[] { action }).ToList();
        var navigated = menu with { BreadCrumb = newCrumb };
        state.Menus[menuId] = navigated;
        return Task.FromResult(navigated);
    }

    public Task<FormState> StartForm(string formId, FormField[] fields, string projectSlug, CancellationToken ct)
    {
        if (fields.Length == 0)
            throw new ArgumentException("Form must have at least one field.", nameof(fields));

        foreach (var f in fields)
            if (f.Type is FormFieldType.SingleChoice or FormFieldType.MultiChoice && (f.Options is null || f.Options.Count == 0))
                throw new ArgumentException($"Field '{f.Name}' of type {f.Type} must have options.", nameof(fields));

        if (state.Forms.TryGetValue(formId, out var existing))
            return Task.FromResult(existing);

        var formState = new FormState
        {
            Id = formId,
            ProjectSlug = projectSlug,
            Fields = fields,
            CurrentField = 0,
            Values = new Dictionary<string, string>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        state.Forms[formId] = formState;

        var firstField = fields[0];
        if (firstField.Type == FormFieldType.FreeText)
            state.PendingFreeText[projectSlug] = formId;

        return Task.FromResult(formState);
    }

    public Task<FormState> AdvanceForm(string formId, string value, CancellationToken ct)
    {
        if (!state.Forms.TryGetValue(formId, out var form))
            throw new KeyNotFoundException($"Form '{formId}' not found.");

        var currentField = form.Fields[form.CurrentField];

        var storedValue = currentField.Type == FormFieldType.MultiChoice
            ? ResolveMultiChoiceValue(form, value)
            : value;

        var updatedValues = new Dictionary<string, string>(form.Values)
        {
            [currentField.Name] = storedValue
        };

        var nextFieldIndex = form.CurrentField + 1;
        var updatedForm = form with
        {
            CurrentField = nextFieldIndex,
            Values = updatedValues
        };

        if (nextFieldIndex >= form.Fields.Count)
        {
            state.Forms.Remove(formId);
            foreach (var key in state.PendingFreeText.Keys.ToList())
                if (state.PendingFreeText[key] == formId)
                    state.PendingFreeText.Remove(key);
        }
        else
        {
            var nextField = form.Fields[nextFieldIndex];
            if (nextField.Type == FormFieldType.FreeText)
                state.PendingFreeText[form.ProjectSlug] = formId;
            else if (state.PendingFreeText.ContainsKey(form.ProjectSlug))
                state.PendingFreeText.Remove(form.ProjectSlug);

            state.Forms[formId] = updatedForm;
        }

        return Task.FromResult(updatedForm);
    }

    static FormState ToggleMultiChoiceSelection(FormState form, string value)
    {
        var currentField = form.Fields[form.CurrentField];
        var existingCsv = form.Values.TryGetValue(currentField.Name, out var csv) ? csv : "";
        var selected = string.IsNullOrEmpty(existingCsv)
            ? new HashSet<string>()
            : new HashSet<string>(existingCsv.Split(','));

        if (!selected.Remove(value))
            selected.Add(value);

        var updatedValues = new Dictionary<string, string>(form.Values)
        {
            [currentField.Name] = string.Join(",", selected)
        };

        return form with { Values = updatedValues };
    }

    static string ResolveMultiChoiceValue(FormState form, string _)
    {
        var currentField = form.Fields[form.CurrentField];
        return form.Values.TryGetValue(currentField.Name, out var csv) ? csv : "";
    }

    static CallbackResult RenderFormFieldResult(FormState form, FormField field)
    {
        if (field.Type == FormFieldType.FreeText)
            return new CallbackResult(field.Prompt, null, null);

        var buttons = field.Options?.Select(o =>
            new Button(o.Text, $"fm:{form.Id}:{o.CallbackData.Split(':').Last()}", o.Url)).ToList();

        if (field.Type == FormFieldType.MultiChoice)
            buttons?.Add(new Button("Done", $"fm:{form.Id}:__done__", null));

        return new CallbackResult(field.Prompt, null, null, buttons);
    }

    static CallbackResult RenderMultiChoiceResult(FormState form)
    {
        var field = form.Fields[form.CurrentField];
        var selectedCsv = form.Values.TryGetValue(field.Name, out var csv) ? csv : "";
        var selected = string.IsNullOrEmpty(selectedCsv)
            ? new HashSet<string>()
            : new HashSet<string>(selectedCsv.Split(','));

        var buttons = field.Options?.Select(o =>
        {
            var val = o.CallbackData.Split(':').Last();
            var prefix = selected.Contains(val) ? "\u2705 " : "";
            return new Button($"{prefix}{o.Text}", $"fm:{form.Id}:{val}", o.Url);
        }).ToList();

        buttons?.Add(new Button("Done", $"fm:{form.Id}:__done__", null));

        var selectedText = selected.Count > 0
            ? $"{field.Prompt}\n\nSelected: {string.Join(", ", selected)}"
            : field.Prompt;

        return new CallbackResult(selectedText, null, null, buttons);
    }

    static void ValidateMenuLabels(MenuNode node)
    {
        if (node.Label == "__back__")
            throw new ArgumentException("Menu node label '__back__' is reserved.");
        if (node.Children is null) return;
        foreach (var child in node.Children)
            ValidateMenuLabels(child);
    }

    static MenuNode? ResolveMenuNode(MenuNode root, IReadOnlyList<string> breadCrumb)
    {
        var current = root;
        foreach (var label in breadCrumb)
        {
            current = current.Children?.FirstOrDefault(c => c.Label == label);
            if (current is null)
                return null;
        }
        return current;
    }

    static CallbackResult RenderPaginatorResult(PaginatorState paginator)
    {
        var pageItems = paginator.Items
            .Skip(paginator.CurrentPage * paginator.PageSize)
            .Take(paginator.PageSize)
            .ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling((double)paginator.Items.Count / paginator.PageSize));
        var lines = pageItems.Select((item, i) =>
            $"{paginator.CurrentPage * paginator.PageSize + i + 1}. {item}");
        var text = string.Join("\n", lines) + $"\n\nPage {paginator.CurrentPage + 1}/{totalPages}";

        var navButtons = new List<Button>();
        if (paginator.CurrentPage > 0)
            navButtons.Add(new Button("\u25c0 Prev", $"pg:{paginator.Id}:prev", null));
        if (paginator.CurrentPage < totalPages - 1)
            navButtons.Add(new Button("Next \u25b6", $"pg:{paginator.Id}:next", null));

        return new CallbackResult(text, null, null, navButtons.Count > 0 ? navButtons : null);
    }

    static CallbackResult RenderMenuResult(MenuState menu)
    {
        var currentNode = ResolveMenuNode(menu.Root, menu.BreadCrumb);

        if (currentNode is null)
            return new CallbackResult(null, null, "Invalid menu path");

        if (currentNode.Action is not null)
            return new CallbackResult(null, currentNode.Action, null);

        if (currentNode.Children is null || currentNode.Children.Count == 0)
        {
            var backBtn = menu.BreadCrumb.Count > 0
                ? new List<Button> { new("\u25c0 Back", $"mn:{menu.Id}:__back__", null) }
                : null;
            return new CallbackResult(currentNode.Label, null, null, backBtn);
        }

        var buttons = currentNode.Children
            .Select(c => new Button(c.Label, $"mn:{menu.Id}:{c.Label}", null))
            .ToList();

        if (menu.BreadCrumb.Count > 0)
            buttons.Add(new Button("\u25c0 Back", $"mn:{menu.Id}:__back__", null));

        var breadcrumbText = menu.BreadCrumb.Count > 0
            ? string.Join(" > ", menu.BreadCrumb)
            : menu.Root.Label;

        return new CallbackResult(breadcrumbText, null, null, buttons);
    }
}
