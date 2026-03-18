using Orleans.Journaling;

namespace Core.Contracts;

public sealed class UserProfileDurableState(
    IDurableDictionary<string, string> preferences,
    IDurableDictionary<string, string> projects)
{
    public IDurableDictionary<string, string> Preferences => preferences;
    public IDurableDictionary<string, string> Projects => projects;
}
