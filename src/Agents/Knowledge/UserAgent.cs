using System.ComponentModel;
using System.Text.Json;
using IAW.Core;
using IAW.Core.AI;
using IAW.Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Knowledge;

public class UserAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<IAW.Core.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IUser
{
    private const string PreferencesKey = "user-preferences";
    private const string MemoriesKey = "user-memories";

    protected override string DisplayName => "User Agent";
    protected override string Instructions =>
        "You are a personal context manager. You store and retrieve user preferences, memories, and personal context.";

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(GetPreferenceTool, nameof(GetPreferenceTool),
                "Get a user preference by key"),
            AIFunctionFactory.Create(SetPreferenceTool, nameof(SetPreferenceTool),
                "Set a user preference"),
            AIFunctionFactory.Create(AddMemoryTool, nameof(AddMemoryTool),
                "Store a new memory for the user"),
            AIFunctionFactory.Create(GetMemoriesTool, nameof(GetMemoriesTool),
                "Retrieve all stored user memories"),
        ];
    }

    public Task<string> GetPreferenceAsync(string key, CancellationToken ct = default)
    {
        var prefs = DeserializePreferences();
        return Task.FromResult(prefs.TryGetValue(key, out var value) ? value : "");
    }

    public async Task SetPreferenceAsync(string key, string value, CancellationToken ct = default)
    {
        var prefs = DeserializePreferences();
        prefs[key] = value;
        State[PreferencesKey] = new StateEntry(PreferencesKey, JsonSerializer.Serialize(prefs));
        await WriteStateAsync(ct);

        await PublishAsync("user.preference.set", new Dictionary<string, object>
        {
            ["Key"] = key
        }, ct);
    }

    public async Task AddMemoryAsync(string memory, CancellationToken ct = default)
    {
        var memories = DeserializeMemories();
        memories.Add(memory);
        State[MemoriesKey] = new StateEntry(MemoriesKey, JsonSerializer.Serialize(memories));
        await WriteStateAsync(ct);

        await PublishAsync("user.memory.added", new Dictionary<string, object>
        {
            ["MemoryCount"] = memories.Count
        }, ct);
    }

    public Task<IReadOnlyList<string>> GetMemoriesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(DeserializeMemories());

    [Description("Get a user preference by key")]
    private Task<string> GetPreferenceTool(
        [Description("The preference key")] string key)
        => GetPreferenceAsync(key);

    [Description("Set a user preference")]
    private async Task<string> SetPreferenceTool(
        [Description("The preference key")] string key,
        [Description("The preference value")] string value)
    {
        await SetPreferenceAsync(key, value);
        return $"Preference '{key}' set.";
    }

    [Description("Store a new memory for the user")]
    private async Task<string> AddMemoryTool(
        [Description("The memory text to store")] string memory)
    {
        await AddMemoryAsync(memory);
        return "Memory stored.";
    }

    [Description("Retrieve all stored user memories")]
    private async Task<string> GetMemoriesTool()
    {
        var memories = await GetMemoriesAsync();
        return memories.Count > 0
            ? string.Join("\n", memories)
            : "No memories stored yet.";
    }

    private Dictionary<string, string> DeserializePreferences()
    {
        if (!State.TryGetValue(PreferencesKey, out var desc))
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(desc.Value.ToString()!)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private List<string> DeserializeMemories()
    {
        if (!State.TryGetValue(MemoriesKey, out var desc))
            return [];
        try { return JsonSerializer.Deserialize<List<string>>(desc.Value.ToString()!) ?? []; }
        catch { return []; }
    }
}
