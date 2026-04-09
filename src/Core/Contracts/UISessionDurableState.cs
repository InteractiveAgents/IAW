using Core.Contracts.UI;
using Orleans.Journaling;

namespace Core.Contracts;

public sealed class UISessionDurableState(
    IDurableDictionary<string, WizardState> wizards,
    IDurableDictionary<string, string> pendingFreeText,
    IDurableDictionary<string, PaginatorState> paginators,
    IDurableDictionary<string, MenuState> menus,
    IDurableDictionary<string, FormState> forms,
    IDurableDictionary<string, PendingOptionSet> pendingOptionSets)
{
    public IDurableDictionary<string, WizardState> Wizards => wizards;
    public IDurableDictionary<string, string> PendingFreeText => pendingFreeText;
    public IDurableDictionary<string, PaginatorState> Paginators => paginators;
    public IDurableDictionary<string, MenuState> Menus => menus;
    public IDurableDictionary<string, FormState> Forms => forms;
    public IDurableDictionary<string, PendingOptionSet> PendingOptionSets => pendingOptionSets;
}