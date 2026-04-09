using Core.Contracts.Security;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public sealed class PolicyContextProvider(
    IGrainFactory grainFactory,
    ILogger<PolicyContextProvider>? logger = null)
    : MessageAIContextProvider
{
    protected override async ValueTask<IEnumerable<Microsoft.Extensions.AI.ChatMessage>> ProvideMessagesAsync(
        MessageAIContextProvider.InvokingContext context, CancellationToken cancellationToken = default)
    {
        var userId = ContextProviderIdentity.ReadUserId();
        if (userId is null)
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

        try
        {
            var approver = grainFactory.GetGrain<IApprover>(userId);
            var policies = await approver.ListPolicies(cancellationToken);
            if (policies.Count == 0)
                return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

            var lines = new List<string> { "## Approver policies" };
            foreach (var p in policies)
                lines.Add($"- [policy:{p.Scope}] {p.Rule}");

            return new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, string.Join("\n", lines)) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Policy context provider failed for user {UserId}", userId);
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();
        }
    }
}
