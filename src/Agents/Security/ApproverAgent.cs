using Core;
using Core.AI;
using Core.Contracts;
using Core.Contracts.Security;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace IAW.Agents.Security;

// ApproverAgent implements IApprover from Core.Contracts.Security.

[Reentrant]
[GrainType(IAWConstants.GrainTypes.Approver)]
public class ApproverAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient,
    ILogger<ApproverAgent> logger)
    : Agent<IApprover>(durableState, chatClient), IApprover
{
    const string PolicyKeyPrefix = "policy:";
    const string PendingKeyPrefix = "pending:";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly ConcurrentDictionary<string, TaskCompletionSource<AuthorizationDecision>> _waiters = new();

    protected override int MaxHistoryMessages => 0;
    protected override bool BypassToolAuthorization => true;
    protected override bool DiscoverInterfaceToolsEnabled => false;
    protected override IReadOnlyList<AITool> DefineTools() => [];
    protected override IReadOnlyList<AITool> DefineAdditionalTools() => [];

    public async Task<AuthorizationDecision> Authorize(ToolAuthorizationRequest request, CancellationToken ct = default)
    {
        var policies = LoadPolicies()
            .Where(p => p.Scope == AuthorizationScope.User
                        || (p.Scope == AuthorizationScope.Thread && p.ThreadId == request.ThreadId))
            .ToList();

        var judgment = await JudgeAsync(request, policies, ct);

        if (judgment.Decision == "allow")
            return new AuthorizationDecision(AuthorizationOutcome.Allow, judgment.Reason);

        if (judgment.Decision == "deny")
        {
            await PublishAsync(IAWConstants.Events.ToolDenied, new Dictionary<string, string>
            {
                [IAWConstants.PayloadKeys.AgentId] = request.AgentId,
                [IAWConstants.PayloadKeys.ToolName] = request.ToolName,
                [IAWConstants.PayloadKeys.Reason] = judgment.Reason,
                [IAWConstants.PayloadKeys.UserId] = request.UserId
            }, ct);

            return new AuthorizationDecision(AuthorizationOutcome.Deny, judgment.Reason);
        }

        var approvalId = $"ap-{Guid.NewGuid().ToString("N")[..12]}";
        var prompt = new ApprovalPrompt(
            approvalId,
            request.UserId,
            request.ThreadId,
            judgment.Question ?? $"Allow {request.AgentDisplayName} to run {request.ToolName}?",
            judgment.Options ?? DefaultOptions(),
            DateTimeOffset.UtcNow);

        State[PendingKeyPrefix + approvalId] = new StateEntry(
            PendingKeyPrefix + approvalId,
            new PendingAuthorizationEntry(prompt, request));
        await WriteStateAsync(ct);

        // Register the waiter BEFORE publishing so a fast reply can't race past it.
        var tcs = new TaskCompletionSource<AuthorizationDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[approvalId] = tcs;

        var optionsJson = JsonSerializer.Serialize(prompt.Options);

        await PublishAsync(IAWConstants.Events.ApprovalRequested, new Dictionary<string, string>
        {
            [IAWConstants.PayloadKeys.ApprovalId] = approvalId,
            [IAWConstants.PayloadKeys.UserId] = request.UserId,
            [IAWConstants.PayloadKeys.Question] = prompt.Question,
            [IAWConstants.PayloadKeys.OptionsJson] = optionsJson,
            [IAWConstants.PayloadKeys.AgentId] = request.AgentId,
            [IAWConstants.PayloadKeys.ToolName] = request.ToolName
        }, ct);

        using (ct.Register(() =>
        {
            _waiters.TryRemove(approvalId, out _);
            tcs.TrySetCanceled();
        }))
        {
            return await tcs.Task;
        }
    }

    public async Task ResolveApproval(string approvalId, string decisionKey, CancellationToken ct = default)
    {
        var stateKey = PendingKeyPrefix + approvalId;
        if (!State.TryGetValue(stateKey, out var entry))
        {
            logger.LogWarning("ResolveApproval: no pending approval {ApprovalId}", approvalId);
            return;
        }

        if (entry.Value is not PendingAuthorizationEntry pending)
        {
            logger.LogWarning("ResolveApproval: malformed pending entry for {ApprovalId}", approvalId);
            State.Remove(stateKey);
            await WriteStateAsync(ct);
            return;
        }

        State.Remove(stateKey);

        var allowed = ApprovalDecisionKeys.IsAllowKey(decisionKey);
        var scope = ApprovalDecisionKeys.KeyToScope(decisionKey);

        if (allowed && scope != AuthorizationScope.Once)
        {
            var rule = await SummarizePolicyAsync(pending.Request, scope, ct);
            var policyId = $"pol-{Guid.NewGuid().ToString("N")[..12]}";
            var policy = new ApproverPolicy(
                policyId, scope,
                scope == AuthorizationScope.Thread ? pending.Request.ThreadId : null,
                rule, DateTimeOffset.UtcNow);
            State[PolicyKeyPrefix + policyId] = new StateEntry(PolicyKeyPrefix + policyId, policy);
        }

        await WriteStateAsync(ct);

        var decision = allowed
            ? new AuthorizationDecision(AuthorizationOutcome.Allow, $"User approved ({scope})", scope)
            : new AuthorizationDecision(AuthorizationOutcome.Deny, "User denied");

        await PublishAsync(IAWConstants.Events.ApprovalResolved, new Dictionary<string, string>
        {
            [IAWConstants.PayloadKeys.ApprovalId] = approvalId,
            [IAWConstants.PayloadKeys.DecisionKey] = decisionKey,
            [IAWConstants.PayloadKeys.UserId] = pending.Request.UserId,
            [IAWConstants.PayloadKeys.AgentId] = pending.Request.AgentId,
            [IAWConstants.PayloadKeys.ToolName] = pending.Request.ToolName
        }, ct);

        if (_waiters.TryRemove(approvalId, out var waiter))
            waiter.TrySetResult(decision);
    }

    public async Task<string> AddPolicy(string scope, string? threadId, string rule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rule))
            return "Rule cannot be empty.";

        var parsedScope = Enum.TryParse<AuthorizationScope>(scope, ignoreCase: true, out var s)
            ? s
            : AuthorizationScope.User;

        var policyId = $"pol-{Guid.NewGuid().ToString("N")[..12]}";
        var policy = new ApproverPolicy(
            policyId, parsedScope,
            parsedScope == AuthorizationScope.Thread ? threadId : null,
            rule, DateTimeOffset.UtcNow);

        State[PolicyKeyPrefix + policyId] = new StateEntry(PolicyKeyPrefix + policyId, policy);
        await WriteStateAsync(ct);

        return $"Policy added ({parsedScope}): {rule}";
    }

    public async Task<string> RemovePolicy(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Query cannot be empty.";

        var policies = LoadPolicies();
        if (policies.Count == 0)
            return "No policies to remove.";

        var matchedId = await MatchPolicyByQueryAsync(query, policies, ct);
        if (matchedId is null)
            return $"No policy matched '{query}'.";

        var removed = policies.First(p => p.Id == matchedId);
        State.Remove(PolicyKeyPrefix + matchedId);
        await WriteStateAsync(ct);

        return $"Removed policy: {removed.Rule}";
    }

    public Task<IReadOnlyList<ApproverPolicy>> ListPolicies(CancellationToken ct = default)
    {
        IReadOnlyList<ApproverPolicy> policies = LoadPolicies();
        return Task.FromResult(policies);
    }

    List<ApproverPolicy> LoadPolicies()
    {
        var result = new List<ApproverPolicy>();
        foreach (var entry in State.Values)
        {
            if (entry.Key.StartsWith(PolicyKeyPrefix) && entry.Value is ApproverPolicy policy)
                result.Add(policy);
        }
        return result;
    }

    static IReadOnlyList<ApprovalOption> DefaultOptions() =>
    [
        new(ApprovalDecisionKeys.AllowOnce, "Yes, once"),
        new(ApprovalDecisionKeys.AllowThread, "Yes, for this conversation"),
        new(ApprovalDecisionKeys.AllowUser, "Yes, always"),
        new(ApprovalDecisionKeys.Deny, "No")
    ];

    async Task<ApproverJudgment> JudgeAsync(
        ToolAuthorizationRequest request, IReadOnlyList<ApproverPolicy> policies, CancellationToken ct)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("TOOL REQUEST:");
        promptBuilder.AppendLine($"  agent: {request.AgentDisplayName} ({request.AgentId})");
        promptBuilder.AppendLine($"  tool:  {request.ToolName}");
        promptBuilder.AppendLine($"  args:  {request.ArgumentsPreview}");
        promptBuilder.AppendLine();

        if (policies.Count > 0)
        {
            promptBuilder.AppendLine("STORED POLICIES (natural language — interpret semantically):");
            foreach (var policy in policies)
                promptBuilder.AppendLine($"  - [{policy.Scope}] {policy.Rule}");
            promptBuilder.AppendLine();
        }

        if (request.RecentTurnSnippets.Count > 0)
        {
            promptBuilder.AppendLine("RECENT CONVERSATION (for language detection and context):");
            foreach (var snippet in request.RecentTurnSnippets)
                promptBuilder.AppendLine($"  > {snippet}");
            promptBuilder.AppendLine();
        }

        promptBuilder.Append("Decide: allow, deny, or ask. Respond with strict JSON only.");

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, Instructions),
            new(ChatRole.User, promptBuilder.ToString())
        };

        try
        {
            var response = await ChatClient.GetResponseAsync(messages, new ChatOptions
            {
                MaxOutputTokens = 512
            }, ct);

            var text = response.Text ?? "";
            return ParseJudgment(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Approver LLM judgment failed for {Tool} — defaulting to ask", request.ToolName);
            return new ApproverJudgment("ask",
                "Unable to evaluate automatically; asking for confirmation.",
                $"Allow {request.AgentDisplayName} to run {request.ToolName}?",
                DefaultOptions());
        }
    }

    static ApproverJudgment ParseJudgment(string llmText)
    {
        var json = ExtractJson(llmText);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var decision = root.TryGetProperty("decision", out var d) ? d.GetString() ?? "ask" : "ask";
            var reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            var question = root.TryGetProperty("question", out var q) ? q.GetString() : null;

            IReadOnlyList<ApprovalOption>? options = null;
            if (root.TryGetProperty("options", out var optsElement) && optsElement.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ApprovalOption>();
                foreach (var item in optsElement.EnumerateArray())
                {
                    var key = item.TryGetProperty("key", out var k) ? k.GetString() : null;
                    var label = item.TryGetProperty("label", out var l) ? l.GetString() : null;
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(label))
                        list.Add(new ApprovalOption(key, label));
                }
                if (list.Count > 0) options = list;
            }

            return new ApproverJudgment(decision.ToLowerInvariant(), reason, question, options);
        }
        catch (JsonException)
        {
            return new ApproverJudgment("ask",
                "LLM response could not be parsed.",
                "Allow this action?",
                null);
        }
    }

    static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        var braceStart = trimmed.IndexOf('{');
        var braceEnd = trimmed.LastIndexOf('}');
        return braceStart >= 0 && braceEnd > braceStart
            ? trimmed[braceStart..(braceEnd + 1)]
            : trimmed;
    }

    async Task<string> SummarizePolicyAsync(ToolAuthorizationRequest request, AuthorizationScope scope, CancellationToken ct)
    {
        var prompt = $"""
            The user has approved this action with scope '{scope}'. Summarize the pattern of
            actions to remember as a short natural-language policy rule (one sentence). Be
            slightly broader than the specific invocation so similar actions match later.

            Agent: {request.AgentDisplayName}
            Tool:  {request.ToolName}
            Args:  {request.ArgumentsPreview}

            Respond with ONE sentence, no quotes, no preamble.
            """;

        try
        {
            var response = await ChatClient.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, prompt)],
                new ChatOptions { MaxOutputTokens = 128 }, ct);

            var rule = (response.Text ?? "").Trim().Trim('"');
            return string.IsNullOrEmpty(rule)
                ? $"Allow {request.AgentDisplayName}.{request.ToolName}"
                : rule;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Policy summarization failed, using literal rule");
            return $"Allow {request.AgentDisplayName}.{request.ToolName}";
        }
    }

    async Task<string?> MatchPolicyByQueryAsync(string query, IReadOnlyList<ApproverPolicy> policies, CancellationToken ct)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Pick the ID of the ONE policy that best matches the user's removal query.");
        promptBuilder.AppendLine("If none clearly matches, reply with NONE.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"Query: {query}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Policies:");
        foreach (var policy in policies)
            promptBuilder.AppendLine($"  {policy.Id} [{policy.Scope}]: {policy.Rule}");
        promptBuilder.AppendLine();
        promptBuilder.Append("Reply with just the policy ID or NONE.");

        try
        {
            var response = await ChatClient.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, promptBuilder.ToString())],
                new ChatOptions { MaxOutputTokens = 64 }, ct);

            var answer = (response.Text ?? "").Trim().Trim('.').Trim();
            if (answer.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return null;

            var match = policies.FirstOrDefault(p =>
                answer.Contains(p.Id, StringComparison.OrdinalIgnoreCase));
            return match?.Id;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Policy match LLM call failed");
            return null;
        }
    }

    [GenerateSerializer]
    public sealed record PendingAuthorizationEntry(
        [property: Id(0)] ApprovalPrompt Prompt,
        [property: Id(1)] ToolAuthorizationRequest Request);

    sealed record ApproverJudgment(
        string Decision,
        string Reason,
        string? Question,
        IReadOnlyList<ApprovalOption>? Options);
}
