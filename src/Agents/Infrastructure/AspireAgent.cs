using System.Diagnostics;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Tools;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Infrastructure;

public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IAspire
{
    protected override string DisplayName => "Aspire";
    protected override string Instructions => """
        You are Aspire, the IAW team's orchestration and deployment infrastructure specialist.
        You manage .NET Aspire resources — listing, starting, stopping, and restarting services.
        You have RunShellAsync and RunDotnetAsync tools — use them to execute Aspire CLI commands.
        When asked about resource health or service management, execute the operation immediately.
        Report resource status, health, and any errors concisely.
        """;

    protected override IReadOnlyList<AITool> DefineTools()
    {
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new ShellTools(() => GetWorkspacePath() ?? Directory.GetCurrentDirectory()));
        return tools;
    }

}
