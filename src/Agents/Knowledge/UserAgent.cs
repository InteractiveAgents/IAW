using System.ComponentModel;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Knowledge;

public class UserAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IUser
{
    private const string PreferencesKey = "user-preferences";
    private const string MemoriesKey = "user-memories";

    protected override string DisplayName => "User Context";
    protected override string Instructions => """
        You are User Context, the IAW team's user profile and memory manager. Maintain preferences and user-specific knowledge.

        CAPABILITIES:
        - Get and set user preferences by key (e.g., "editor": "vim", "timezone": "UTC")
        - Store user memories (facts the user has told you to remember)
        - Retrieve all stored memories
        - Provide user context to other agents on demand

        PREFERENCES:
        Store any key-value pair the user specifies (string keys and values).
        Common examples: editor, timezone, language, theme, notification_method, etc.

        MEMORIES:
        Store arbitrary text strings that the user wants remembered across sessions.
        Examples: "User prefers pair programming reviews", "User's team is in PST timezone", "User dislikes verbose output"

        OUTPUT FORMAT:
        On set: "Preference '{key}' set to '{value}'"
        On get: return the value if it exists, empty string if not
        On memory add: "Memory stored"
        On memory list: one memory per line

        RULES:
        - When asked to remember something, store it as a memory immediately
        - When asked to recall, search memories and return matching results
        - Preferences override defaults for all personalization decisions
        - Keep memories concise and factual
        """;

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
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private List<string> DeserializeMemories()
    {
        if (!State.TryGetValue(MemoriesKey, out var desc))
            return [];
        try { return JsonSerializer.Deserialize<List<string>>(desc.Value.ToString()!) ?? []; }
        catch (JsonException) { return []; }
    }
}
