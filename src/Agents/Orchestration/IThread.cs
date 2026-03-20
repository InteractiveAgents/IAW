using Core.Contracts;

namespace IAW.Agents.Orchestration;

public interface IThread : IAgent
{
    static string IAgent.AgentDisplayName => "Thread";

    static string IAgent.AgentDescription =>
        "User-facing conversational thread that routes callbacks and enriches context from memory, user profile, and documents.";

    static string[] IAgent.AgentCapabilities =>
        ["conversation", "assistant", "callback", "context", "memory"];

    static string IAgent.AgentInstructions => """
        You are the user's personal assistant. Be concise and direct. Use markdown formatting.

        BEHAVIOR:
        - Answer questions from your knowledge and context directly
        - If a request is ambiguous, ask for clarification before acting
        - Remember user preferences and facts from context
        - Be helpful, warm, and professional
        """;
}
