using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace IAW.Agents.Personal;

public class PreferenceAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent<IPreference>(durableState, chatClient), IPreference
{
    protected override int MaxHistoryMessages => 20;

    public async Task SetRuleAsync(PreferenceRule rule, CancellationToken ct = default)
    {
        var key = $"pref:{rule.Category}:{rule.Rule.GetHashCode():X8}";
        var json = JsonSerializer.Serialize(rule);
        State[key] = new StateEntry(key, json);
        await WriteStateAsync(ct);
    }

    public async Task RemoveRuleAsync(string category, string rule, CancellationToken ct = default)
    {
        var key = $"pref:{category}:{rule.GetHashCode():X8}";
        State.Remove(key);
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<PreferenceRule>> GetRulesAsync(string? category = null, CancellationToken ct = default)
    {
        var rules = State
            .Where(kvp => kvp.Key.StartsWith("pref:"))
            .Select(kvp => DeserializeRule(kvp.Value.Value))
            .Where(r => r is not null && (category is null || r.Category == category))
            .Cast<PreferenceRule>()
            .ToList();

        return Task.FromResult<IReadOnlyList<PreferenceRule>>(rules);
    }

    public Task<IReadOnlyList<PreferenceRule>> GetAllRulesAsync(CancellationToken ct = default)
        => GetRulesAsync(null, ct);

    private static PreferenceRule? DeserializeRule(object value)
    {
        if (value is PreferenceRule rule)
            return rule;

        if (value is string json)
            return JsonSerializer.Deserialize<PreferenceRule>(json);

        // handle JsonElement from Orleans deserialization
        if (value is JsonElement element)
            return JsonSerializer.Deserialize<PreferenceRule>(element.GetRawText());

        return null;
    }
}
