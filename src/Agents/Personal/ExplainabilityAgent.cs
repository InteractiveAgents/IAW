using Core.Contracts;
using IAW.Agents.Memory;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Personal;

public class ExplainabilityAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent<IExplainability>(durableState, chatClient), IExplainability
{
    protected override int MaxHistoryMessages => 20;

    public async Task<IReadOnlyList<MemoryTrace>> SearchAllMemoriesAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        var traces = new List<MemoryTrace>();

        await SearchMemoryLayer<IEpisodeMemory>("episode-memory", "EpisodeMemory", query, topK, traces, ct);
        await SearchMemoryLayer<IProjectMemory>("project-memory", "ProjectMemory", query, topK, traces, ct);
        await SearchMemoryLayer<IUserMemory>("user-memory", "UserMemory", query, topK, traces, ct);
        await SearchPreferencesAsync(query, traces, ct);
        await SearchKnowledgeAsync(query, traces, ct);

        return traces;
    }

    public async Task<ExplanationResult> ExplainAsync(string question, CancellationToken ct = default)
    {
        var traces = await SearchAllMemoriesAsync(question, topK: 5, ct);

        if (traces.Count == 0)
        {
            return new ExplanationResult(question,
                "I couldn't find any relevant memories, preferences, or decisions related to this question.",
                traces);
        }

        var traceContext = string.Join("\n", traces.Select((t, i) =>
            $"[{i + 1}] ({t.MemoryType}) {t.Content} — source: {t.Source}"));

        var prompt = $"""
            The user asked: "{question}"

            Here are relevant memories, preferences, and decisions I found:
            {traceContext}

            Synthesize a clear explanation that:
            1. Directly answers the question
            2. Cites specific sources by number [1], [2], etc.
            3. Mentions dates and conversations when available
            4. Is concise but thorough
            """;

        var response = await ChatClient.GetResponseAsync(
            [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt)],
            cancellationToken: ct);

        return new ExplanationResult(question, response.Text ?? "Unable to generate explanation.", traces);
    }

    private async Task SearchMemoryLayer<TMemory>(
        string agentId, string layerName, string query, int topK, List<MemoryTrace> traces, CancellationToken ct)
        where TMemory : IMemoryAgent
    {
        try
        {
            var memoryAgent = GrainFactory.GetGrain<TMemory>(agentId);
            var results = await memoryAgent.SearchAsync(query, topK, ct);
            foreach (var entry in results)
                traces.Add(new MemoryTrace(layerName, entry.Content, $"{layerName.ToLowerInvariant()}:{entry.Source.Source}"));
        }
        catch (OperationCanceledException) { throw; }
        catch { /* memory agent may not have data or embedder not available */ }
    }

    private async Task SearchPreferencesAsync(string query, List<MemoryTrace> traces, CancellationToken ct)
    {
        try
        {
            var prefAgent = GrainFactory.GetGrain<IPreference>("preferences");
            var rules = await prefAgent.GetAllRulesAsync(ct);
            foreach (var rule in rules)
            {
                if (rule.Rule.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (rule.Reason?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    traces.Add(new MemoryTrace(
                        "Preference",
                        $"[{rule.Category}] {rule.Rule} (reason: {rule.Reason})",
                        $"preference:{rule.Category}"));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* preference agent may not be active */ }
    }

    private async Task SearchKnowledgeAsync(string query, List<MemoryTrace> traces, CancellationToken ct)
    {
        try
        {
            var knowledge = GrainFactory.GetGrain<IKnowledge>("knowledge");
            var decisions = await knowledge.GetDecisions();
            foreach (var d in decisions)
            {
                if (d.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || d.Rationale.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || d.Outcome.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    traces.Add(new MemoryTrace(
                        "Decision",
                        $"[{d.Timestamp:yyyy-MM-dd}] {d.Title}: {d.Rationale} -> {d.Outcome}",
                        $"knowledge:decision:{d.Title}"));
                }
            }

            var patterns = await knowledge.GetPatterns();
            foreach (var p in patterns)
            {
                if (p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    traces.Add(new MemoryTrace(
                        "Pattern",
                        $"{p.Name}: {p.Description}",
                        $"knowledge:pattern:{p.Name}"));
                }
            }

            var conventions = await knowledge.GetConventions();
            foreach (var c in conventions)
            {
                if (c.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    traces.Add(new MemoryTrace(
                        "Convention",
                        c,
                        "knowledge:convention"));
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* knowledge agent may not be active */ }
    }
}
