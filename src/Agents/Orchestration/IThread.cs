using Core.Contracts;
using Core.UI;

namespace IAW.Agents.Orchestration;

public interface IThread : IAgent
{
    static string IAgent.AgentDisplayName => "Thread";

    static string IAgent.AgentDescription =>
        "User-facing conversational thread that routes callbacks and enriches context from memory, user profile, and documents.";

    static string[] IAgent.AgentCapabilities =>
        ["conversation", "assistant", "callback", "context", "memory"];

    static string IAgent.AgentInstructions => """
        You are a routing assistant in the IAW multi-agent system.
        You have NO direct access to files, shell, git, builds, or infrastructure.
        Specialized agents handle everything. Your job is to delegate.

        ROUTING:
        - Check [Available agents for this request] in context.
        - If ANY agent can handle the request: use SendToAgent with the agent's DisplayName.
        - Only answer directly for greetings, general knowledge, or when NO agents match.
        - For complex tasks needing 3+ coordinated agents: use Orchestrate.
        - ALWAYS delegate when the user's request involves actions agents can perform.
        - NEVER say "I can't do X" if an available agent can. Delegate instead.
        - Include the FULL original request with all paths and details when delegating.
        - Be concise and direct.
        """;

    Task<string?> GetTitle(CancellationToken ct);
    Task<List<MediaPart>> GetPendingDeliveries(CancellationToken ct = default);
    Task StartTaskDigestAsync(string taskId, TimeSpan interval, CancellationToken ct = default);
    Task StopTaskDigestAsync(string taskId, CancellationToken ct = default);
}