using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Core.V3;

[GrainType("dynamic-agent-v3")]
public class DynamicAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IDynamicAgent
{
    protected override string Instructions =>
        State.TryGetValue("config-system-prompt", out var entry)
            ? entry.Value.ToString() ?? "You are a helpful assistant."
            : "You are a helpful assistant.";

    protected override string DisplayName =>
        State.TryGetValue("config-display-name", out var entry)
            ? entry.Value.ToString() ?? "Dynamic Agent"
            : "Dynamic Agent";

    protected override AgentKind AgentKindValue => AgentKind.Dynamic;

    public async Task ConfigureAsync(AgentConfiguration config, CancellationToken ct)
    {
        if (config.DisplayName is not null)
            State["config-display-name"] = new StateEntry("config-display-name", config.DisplayName);
        if (config.SystemPrompt is not null)
            State["config-system-prompt"] = new StateEntry("config-system-prompt", config.SystemPrompt);
        if (config.ToolNames is not null)
            State["config-tool-names"] = new StateEntry("config-tool-names", string.Join(",", config.ToolNames));
        if (config.WorkspacePath is not null)
            await SetWorkspaceAsync(config.WorkspacePath, ct);
        await WriteStateAsync(ct);
    }
}
