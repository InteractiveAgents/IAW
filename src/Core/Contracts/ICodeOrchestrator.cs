namespace Core.Contracts;

public interface ICodeOrchestrator : IAgent
{
    static string IAgent.AgentDisplayName => "Code Orchestrator";

    static string IAgent.AgentDescription =>
        "Generates and executes standalone C# console apps that call agent grains directly to fulfill complex orchestration tasks.";

    static string[] IAgent.AgentCapabilities =>
        ["orchestrate", "execute", "generate", "csharp", "code", "automate"];

    [ResponseTimeout("00:15:00")]
    Task<OrchestrationResult> ExecuteCodeOrchestration(string plan, IReadOnlyList<string> selectedAgents, string projectKey, CancellationToken ct = default);
}
