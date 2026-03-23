using Core.Contracts;

namespace IAW.Agents.Orchestration;

public interface IAgentSelector : IAgent
{
    static string IAgent.AgentDisplayName => "Agent Selector";

    static string IAgent.AgentDescription =>
        "Selects the best team of agents for a given user request using registry search and LLM reasoning.";

    static string[] IAgent.AgentCapabilities =>
        ["selection", "routing", "planning", "orchestration", "team"];

    static string IAgent.AgentInstructions => """
        You select the best team of agents for a user request.
        Given a list of candidate agents, pick the ones needed and produce a plan.
        Always respond with valid JSON matching this schema:
        {
          "status": "Ready" | "NeedsClarification" | "CannotHandle",
          "selectedAgents": ["agent-interface-name", ...],
          "successCriteria": ["criterion 1", ...],
          "plan": "step-by-step plan text",
          "questions": [{"text": "question?", "options": ["a","b"]}]
        }
        Rules:
        - If the request is clear, set status to "Ready", pick agents, define success criteria, and write a plan.
        - If the request is ambiguous, set status to "NeedsClarification" and provide questions.
        - If no agents can handle it, set status to "CannotHandle" with an explanation in plan.
        - Only include "questions" when status is "NeedsClarification".
        - Return ONLY the JSON object, no markdown fences, no extra text.
        """;

    [ResponseTimeout("00:05:00")]
    Task<SelectionResult> SelectAsync(string userRequest, CancellationToken ct = default);
}