using Core.Contracts;
using Core.Contracts.Security;
using Core.Memory;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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

        await SearchApproverPoliciesAsync(query, traces, ct);
        await SearchMemoryLookupAsync(query, traces, ct);
        return traces;
    }

    private async Task SearchMemoryLookupAsync(string query, List<MemoryTrace> traces, CancellationToken ct)
    {
        var userId = ExtractUserId();
        if (userId is null) return;

        var lookup = ServiceProvider.GetService<IMemoryLookup>();
        if (lookup is null) return;

        try
        {
            var hit = await lookup.LookupOriginAsync(userId, query, ct);
            if (hit is not null)
                traces.Add(new MemoryTrace(
                    "Memory",
                    $"[{hit.CreatedAt:yyyy-MM-dd}] {hit.Role}: {hit.Content}",
                    hit.SourceTelegramMsgId is not null ? $"telegram:{hit.SourceTelegramMsgId}" : "memory"));
        }
        catch (OperationCanceledException) { throw; }
        catch { /* memory provider may not be configured or qdrant unreachable */ }
    }

    public async Task<ExplanationResult> ExplainAsync(string question, CancellationToken ct = default)
    {
        var traces = await SearchAllMemoriesAsync(question, topK: 5, ct);

        if (traces.Count == 0)
        {
            return new ExplanationResult(question,
                "I couldn't find any relevant policies, memories, or decisions related to this question.",
                traces);
        }

        var traceContext = string.Join("\n", traces.Select((t, i) =>
            $"[{i + 1}] ({t.MemoryType}) {t.Content} — source: {t.Source}"));

        var prompt = $"""
            The user asked: "{question}"

            Here are relevant policies, memories, and decisions I found:
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

    private async Task SearchApproverPoliciesAsync(string query, List<MemoryTrace> traces, CancellationToken ct)
    {
        var userId = ExtractUserId();
        if (userId is null) return;

        try
        {
            var approver = GrainFactory.GetGrain<IApprover>(userId);
            var policies = await approver.ListPolicies(ct);
            foreach (var policy in policies)
            {
                if (policy.Rule.Contains(query, StringComparison.OrdinalIgnoreCase))
                    traces.Add(new MemoryTrace(
                        "Policy",
                        $"[{policy.Scope}] {policy.Rule}",
                        $"approver:{policy.Id}"));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* approver may not be active for this user yet */ }
    }

    private string? ExtractUserId()
    {
        var key = this.GetPrimaryKeyString();
        var slash = key.IndexOf('/');
        var head = slash > 0 ? key[..slash] : key;
        return long.TryParse(head, out _) ? head : null;
    }
}
