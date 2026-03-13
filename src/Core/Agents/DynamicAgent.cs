using Core.Contracts;
using Microsoft.Extensions.AI;
using IAW.Core;

namespace Core.Agents;

[GrainType("dynamic-agent-v3")]
public class DynamicAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IDynamicAgent
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
            await SetWorkspace(config.WorkspacePath, ct);
        await WriteStateAsync(ct);
    }
}
