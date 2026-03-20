using System.Text.Json;
using Core;
using Core.Contracts;
using Core.Registry;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

[GrainType(IAWConstants.GrainTypes.Agent)]
public class AgentSelectorAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent<IAgentSelector>(durableState, chatClient), IAgentSelector
{
    public async Task<SelectionResult> SelectAsync(string userRequest, CancellationToken ct = default)
    {
        var registry = GrainFactory.GetGrain<IAgentRegistry>("global");
        List<AgentCandidate> candidates;

        try
        {
            candidates = await registry.SearchAsync(userRequest, ct: ct);
        }
        catch
        {
            candidates = [];
        }

        var candidateBlock = candidates.Count > 0
            ? string.Join("\n", candidates.Select(c => $"- {c.InterfaceName} ({c.Namespace}): {c.Description} [score={c.Score:F2}]"))
            : "No candidates found in the registry.";

        var prompt = $"""
            User request: {userRequest}

            Available agents:
            {candidateBlock}

            Select the best team and produce a plan.
            """;

        var llmResponse = await GetResponse(prompt, ct);

        return ParseSelectionResult(llmResponse);
    }

    static SelectionResult ParseSelectionResult(string llmResponse)
    {
        var trimmed = llmResponse.Trim();

        // strip markdown code fences if present
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];
        }
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3].TrimEnd();

        try
        {
            var json = JsonDocument.Parse(trimmed);
            var root = json.RootElement;

            var status = ParseStatus(root.TryGetProperty("status", out var s) ? s.GetString() : null);

            var selectedAgents = new List<string>();
            if (root.TryGetProperty("selectedAgents", out var agents) && agents.ValueKind == JsonValueKind.Array)
                selectedAgents.AddRange(agents.EnumerateArray().Select(e => e.GetString()!));

            var successCriteria = new List<string>();
            if (root.TryGetProperty("successCriteria", out var criteria) && criteria.ValueKind == JsonValueKind.Array)
                successCriteria.AddRange(criteria.EnumerateArray().Select(e => e.GetString()!));

            var plan = root.TryGetProperty("plan", out var p) ? p.GetString() : null;

            List<ClarificationQuestion>? questions = null;
            if (root.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array)
            {
                questions = [];
                foreach (var qe in q.EnumerateArray())
                {
                    var text = qe.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    List<string>? options = null;
                    if (qe.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                        options = opts.EnumerateArray().Select(o => o.GetString()!).ToList();
                    questions.Add(new ClarificationQuestion(text, options));
                }
            }

            return new SelectionResult(status, selectedAgents, successCriteria, plan, questions);
        }
        catch (JsonException)
        {
            return new SelectionResult(
                SelectionStatus.CannotHandle,
                [],
                [],
                llmResponse,
                null);
        }
    }

    static SelectionStatus ParseStatus(string? value) => value switch
    {
        "Ready" => SelectionStatus.Ready,
        "NeedsClarification" => SelectionStatus.NeedsClarification,
        "CannotHandle" => SelectionStatus.CannotHandle,
        _ => SelectionStatus.CannotHandle
    };
}
