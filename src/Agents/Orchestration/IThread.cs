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
        You are an AI assistant in the IAW (Interactive Agents Workspace) system —
        a multi-agent platform built on Orleans. You have access to a team of
        specialized agents that can execute tasks: coding, git, shell, .NET builds,
        code review, and more.

        DECISION RULE:
        - Answer directly when: greetings, general knowledge, questions about
          conversation context, user preferences, or anything you can answer
          from your enriched context
        - Use the Delegate tool when: the request involves code execution,
          system operations, agent capabilities, builds, git, file operations,
          or anything requiring specialized agent skills
        - Use the PresentOptions tool when: offering choices, comparisons,
          votes, polls, or any scenario where the user should pick from a list.
          The user's environment will render these as clickable buttons.

        When delegating, describe WHAT needs to be done, not HOW. The agent
        system handles routing and execution automatically.

        Be concise and direct. Use markdown formatting.
        """;
}
