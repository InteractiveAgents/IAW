using Core.Contracts.Security;
using Core.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace IAW.Core;

public abstract partial class Agent
{
    static readonly HashSet<string> SecretArgKeyHints =
    [
        "password", "token", "secret", "apikey", "api_key", "authorization"
    ];

    static readonly JsonSerializerOptions ArgSerializerOptions = new()
    {
        WriteIndented = false
    };

    // Returns the Approver grain key (the user id) this agent's tool calls should be judged against,
    // or null to bypass gating entirely (used by the Approver itself to avoid self-recursion).
    protected virtual string? ResolveApproverGrainKey()
    {
        var grainId = this.GetPrimaryKeyString();
        var slashIndex = grainId.IndexOf('/');
        var head = slashIndex > 0 ? grainId[..slashIndex] : grainId;
        return long.TryParse(head, out _) ? head : null;
    }

    protected async ValueTask<object?> ToolApprovalMiddleware(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        var toolName = context.Function.Name;
        var approverKey = ResolveApproverGrainKey();
        if (approverKey is null)
            return await next(context, cancellationToken);

        var argumentsJson = SerializeArguments(context.Arguments);
        var recentMessages = FormatRecentMessagesForApprover(context.Messages);
        var request = new ToolAuthorizationRequest(
            this.GetPrimaryKeyString(),
            DisplayName,
            toolName,
            argumentsJson,
            recentMessages);

        try
        {
            var approver = GrainFactory.GetGrain<IApprover>(approverKey);
            var decision = await approver.Authorize(request, cancellationToken);

            if (decision.Outcome == AuthorizationOutcome.Allow)
                return await next(context, cancellationToken);

            AgentTelemetry.ApproverDenies.Add(1, new TagList
            {
                { "agent.type", GetType().Name },
                { "tool.name", toolName }
            });
            context.Terminate = true;
            return $"[Action blocked by Approver: {decision.Reason}]";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AgentTelemetry.ApproverFailures.Add(1, new TagList
            {
                { "agent.type", GetType().Name },
                { "tool.name", toolName },
                { "error.type", ex.GetType().Name }
            });
            Logger.LogError(ex, "Approver grain failed for tool {Tool} on agent {AgentId} — failing closed (deny)",
                toolName, this.GetPrimaryKeyString());
            context.Terminate = true;
            return $"[Action blocked: Approver unavailable ({ex.GetType().Name})]";
        }
    }

    static string SerializeArguments(AIFunctionArguments? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return "{}";

        try
        {
            var dict = arguments.ToDictionary(kv => kv.Key, kv => kv.Value);
            var json = JsonSerializer.Serialize(dict, ArgSerializerOptions);
            var redacted = RedactSecretArgs(json);
            return redacted.Length > 2000 ? redacted[..2000] + "\"...(truncated)\"" : redacted;
        }
        catch
        {
            return $"{{\"_argCount\":{arguments.Count}}}";
        }
    }

    static string RedactSecretArgs(string input)
    {
        var redacted = SecretKeyValuePattern().Replace(input, m =>
        {
            var key = m.Groups[1].Value;
            return SecretArgKeyHints.Any(hint => key.Contains(hint, StringComparison.OrdinalIgnoreCase))
                ? $"\"{key}\":\"[REDACTED]\""
                : m.Value;
        });
        return BearerPattern().Replace(redacted, "Bearer [REDACTED]");
    }

    static IReadOnlyList<string> FormatRecentMessagesForApprover(IList<AIChatMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
            return Array.Empty<string>();

        var snippets = new List<string>();
        foreach (var msg in messages.TakeLast(3))
        {
            var text = msg.Text ?? "";
            if (text.Length > 160) text = text[..157] + "...";
            if (text.Length == 0) continue;
            snippets.Add($"{msg.Role}: {text}");
        }
        return snippets;
    }

    [GeneratedRegex("\"([^\"\\\\]+)\"\\s*:\\s*\"[^\"]*\"")]
    private static partial Regex SecretKeyValuePattern();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();
}
