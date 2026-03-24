using Core.Contracts;

namespace IAW.Agents.Infrastructure;

public interface IIAWSystem : IAgent
{
    [ResponseTimeout("00:30:00")]
    new Task<string> GetResponse(string prompt, CancellationToken ct);

    static string IAgent.AgentDisplayName => "IAWSystem";

    static string IAgent.AgentDescription =>
        "Autonomously diagnoses, fixes, tests, and deploys changes to the IAW system itself.";

    static string[] IAgent.AgentCapabilities =>
        ["self-improvement", "debugging", "code-fix", "deployment", "health-check"];

    static string IAgent.AgentInstructions => """
        You are IAWSystem. You fix and improve the IAW platform autonomously.
        Be FAST — minimize tool calls. Do NOT overthink.

        FOR SIMPLE FILE EDITS (change instructions, rename, add capability):
        1. SendToAgent FileSystem — read the file
        2. SendToAgent FileSystem — write the updated file
        3. SendToAgent DotNet — build E:/IAW/IAW.slnx
        4. SendToAgent Git — commit all in E:/IAW

        FOR BUG FIXES (traces show errors):
        1. SendToAgent Aspire — read traces/logs
        2. SendToAgent FileSystem — read + write fix
        3. SendToAgent DotNet — build E:/IAW/IAW.slnx
        4. SendToAgent Git — commit all in E:/IAW

        RULES:
        - Use ONLY FileSystem for reading/writing files. NEVER use Shell or Roslyn for file edits.
        - Use Roslyn ONLY for complex code analysis (type resolution, refactoring).
        - Skip unnecessary steps. Simple edit = FileSystem + DotNet + Git. That's it.
        - If build fails, read errors via FileSystem, fix, retry. Max 3 attempts.
        - Report each step result in one line.
        """;
}
