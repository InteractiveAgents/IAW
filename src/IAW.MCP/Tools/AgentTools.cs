using Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

internal sealed class AgentTools(IClusterClient orleans)
{
    private static readonly string[] WellKnownAgentIds =
    [
        "personal-assistant",
        "roslyn",
        "dotnet",
        "nuget",
        "github",
        "reviewer",
        "self-improvement",
        "fs",
        "shell",
        "git",
        "build",
        "knowledge",
        "user",
        "planning",
        "notification"
    ];

    [McpServerTool(Name = "agent_list_all")]
    [Description("List all registered agents with their metadata and capabilities.")]
    public async Task<string> AgentListAll(CancellationToken ct)
    {
        var results = new List<AgentMetadata>();
        foreach (var id in WellKnownAgentIds)
        {
            var agent = orleans.GetGrain<IAgent>(id);
            var metadata = await agent.GetMetadataAsync(ct);
            results.Add(metadata);
        }

        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "assistant_chat")]
    [Description("Send a message to the PersonalAssistant agent. Records the message in the agent's conversation history.")]
    public async Task<string> AssistantChat(
        [Description("The message to send to the assistant")] string message,
        CancellationToken ct)
    {
        var assistant = orleans.GetGrain<IAgent>("personal-assistant");
        await assistant.AddHistoryAsync("user", message, ct);
        var history = await assistant.GetHistoryAsync(ct);
        return JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
    }
}
