namespace Core.Contracts;

public interface IPreference : IAgent
{
    static string IAgent.AgentDisplayName => "Preference";
    static string IAgent.AgentDescription => "Stores user corrections as behavioral rules and injects them into agent context.";
    static string[] IAgent.AgentCapabilities => ["preferences", "rules", "behavior", "personalization"];
    static string IAgent.AgentInstructions =>
        "You are the Preference Agent. You store and manage user behavioral rules. " +
        "When the user gives you a preference, store it with the appropriate category: " +
        "testing, architecture, style, communication, or tools.";

    Task SetRuleAsync(PreferenceRule rule, CancellationToken ct = default);
    Task RemoveRuleAsync(string category, string rule, CancellationToken ct = default);
    Task<IReadOnlyList<PreferenceRule>> GetRulesAsync(string? category = null, CancellationToken ct = default);
    Task<IReadOnlyList<PreferenceRule>> GetAllRulesAsync(CancellationToken ct = default);
}
