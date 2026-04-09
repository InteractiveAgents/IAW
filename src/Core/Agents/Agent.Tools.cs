using Core;
using Core.Communication;
using Core.Contracts;
using Core.Contracts.Security;
using Core.Tools;
using Core.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;

namespace IAW.Core;

public abstract partial class Agent
{
    private IReadOnlyList<AITool>? _cachedTools;
    private readonly List<UIPart> _pendingUIHints = [];

    private static readonly HashSet<string> ExcludedMethodNames = BuildExcludedMethodNames();

    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    protected virtual IReadOnlyList<AITool> DefineAdditionalTools() => [];

    protected virtual bool BypassToolAuthorization => false;

    protected virtual bool DiscoverInterfaceToolsEnabled => true;

    // Tool names that must never be gated through the Approver — meta/UI helpers that are
    // implicitly consented by calling them.
    private static readonly HashSet<string> GateExemptToolNames =
    [
        nameof(ProposeOptions),
        "AddApproverPolicy",
        "RemoveApproverPolicy",
        "ListApproverPolicies"
    ];

    protected IReadOnlyList<UIPart> DrainPendingUIHints()
    {
        if (_pendingUIHints.Count == 0) return Array.Empty<UIPart>();
        var copy = _pendingUIHints.ToArray();
        _pendingUIHints.Clear();
        return copy;
    }

    protected void ClearPendingUIHints() => _pendingUIHints.Clear();

    [Description("Propose a set of options for the user to choose from. The user sees these as buttons in their chat UI and may tap one OR type a custom response. Use this whenever you need the user to make a choice — NEVER format options inline as A)/B) or 1./2. in your text.")]
    protected string ProposeOptions(
        [Description("The question or prompt shown above the buttons")] string prompt,
        [Description("Up to 8 short option labels. Keep each under 40 characters.")] string[] options)
    {
        if (options is null || options.Length == 0)
            return "ProposeOptions called with no options — nothing to render.";

        var trimmed = options.Take(8).Select(o => o?.Trim() ?? "").Where(o => o.Length > 0).ToList();
        if (trimmed.Count == 0)
            return "ProposeOptions called with only empty labels.";

        var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
        var optionList = trimmed.Select((label, index) =>
            new Option(label.Length > 40 ? label[..37] + "..." : label, (index + 1).ToString())).ToList();

        _pendingUIHints.Add(new OptionsPart(prompt ?? "", optionList, callbackId));
        return $"Options prepared for the user: {string.Join(" | ", trimmed)}";
    }

    private IReadOnlyList<AITool> GetAllTools()
    {
        if (_cachedTools is not null)
            return _cachedTools;

        var tools = new List<AITool>();

        var workspaceTools = new WorkspaceTools(
            () => GetWorkspacePath() ?? ".",
            path =>
            {
                durableState.State[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path);
                _cachedTools = null;
                // state persists at next WriteStateAsync — journaling tracks this mutation automatically
            });
        RegisterToolMethods(tools, workspaceTools);
        RegisterSchedulingTools(tools);

        if (DiscoverInterfaceToolsEnabled)
            DiscoverInterfaceTools(tools);

        tools.AddRange(DefineTools());
        tools.AddRange(DefineAdditionalTools());

        _cachedTools = BypassToolAuthorization
            ? tools
            : WrapWithAuthorizationGate(tools);
        return _cachedTools;
    }

    protected AITool CreateProposeOptionsTool()
    {
        var proposeMethod = typeof(Agent).GetMethod(
            nameof(ProposeOptions),
            BindingFlags.NonPublic | BindingFlags.Instance);
        return AIFunctionFactory.Create(proposeMethod!, this);
    }

    private IReadOnlyList<AITool> WrapWithAuthorizationGate(IReadOnlyList<AITool> tools)
    {
        return tools.Select<AITool, AITool>(tool =>
        {
            if (tool is not AIFunction function)
                return tool;
            if (GateExemptToolNames.Contains(function.Name))
                return tool;
            return new GatedAIFunction(function, (toolName, preview, ct) => AuthorizeToolCallAsync(toolName, preview, ct));
        }).ToList();
    }

    private async Task<GateResult> AuthorizeToolCallAsync(string toolName, string argumentsPreview, CancellationToken ct)
    {
        var grainId = this.GetPrimaryKeyString();
        var userId = ExtractUserIdFromGrainKey(grainId);
        if (userId is null)
            return GateResult.Allow();

        var threadId = ExtractThreadIdFromGrainKey(grainId) ?? grainId;

        try
        {
            var approver = GrainFactory.GetGrain<IApprover>(userId);
            var history = await CollectRecentTurnSnippets(threadId, maxSnippets: 3, ct);
            var request = new ToolAuthorizationRequest(
                grainId, DisplayName, toolName, argumentsPreview,
                ThreadId: threadId,
                UserId: userId,
                RecentTurnSnippets: history);

            var decision = await approver.Authorize(request, ct);
            return decision.Outcome == AuthorizationOutcome.Allow
                ? GateResult.Allow()
                : GateResult.Deny(decision.Reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Authorization check failed for {Tool}, allowing by default", toolName);
            return GateResult.Allow();
        }
    }

    private async Task<IReadOnlyList<string>> CollectRecentTurnSnippets(string threadId, int maxSnippets, CancellationToken ct)
    {
        // Prefer the Thread grain's history (where the user's actual conversation lives) so the
        // Approver LLM can detect the user's language for localized option labels.
        try
        {
            if (threadId != this.GetPrimaryKeyString())
            {
                var threadGrain = GrainFactory.GetGrain<IAgent>(Orleans.Runtime.GrainId.Create("thread", threadId));
                var messages = await threadGrain.GetHistory(ct);
                return FormatHistorySnippets(messages, maxSnippets);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to read thread history for {ThreadId}, falling back to local history", threadId);
        }

        return FormatHistorySnippets(durableState.History.ToList(), maxSnippets);
    }

    private static IReadOnlyList<string> FormatHistorySnippets(IReadOnlyList<global::Core.Contracts.ChatMessage> messages, int maxSnippets)
    {
        var snippets = new List<string>();
        foreach (var msg in messages.TakeLast(maxSnippets))
        {
            var text = msg.Text ?? "";
            if (text.Length > 120) text = text[..117] + "...";
            snippets.Add($"{msg.Role}: {text}");
        }
        return snippets;
    }

    private static string? ExtractUserIdFromGrainKey(string grainId)
    {
        var slashIndex = grainId.IndexOf('/');
        if (slashIndex > 0)
        {
            var head = grainId[..slashIndex];
            return long.TryParse(head, out _) ? head : null;
        }
        return long.TryParse(grainId, out _) ? grainId : null;
    }

    private static string? ExtractThreadIdFromGrainKey(string grainId)
    {
        // Sub-agent grain keys look like "{userId}/{threadSlug}/{InterfaceName}".
        // The thread grain itself is keyed "{userId}/{threadSlug}".
        // Strip the trailing interface segment when present so thread-scoped policies match.
        var firstSlash = grainId.IndexOf('/');
        if (firstSlash <= 0 || !long.TryParse(grainId[..firstSlash], out _))
            return null;

        var lastSlash = grainId.LastIndexOf('/');
        if (lastSlash == firstSlash)
            return grainId;

        var trailing = grainId[(lastSlash + 1)..];
        // An interface-shaped trailing segment starts with 'I' and contains only letters/digits.
        if (trailing.Length > 1 && trailing[0] == 'I' && trailing.Skip(1).All(char.IsLetterOrDigit))
            return grainId[..lastSlash];

        return grainId;
    }


    private void DiscoverInterfaceTools(List<AITool> tools)
    {
        var agentInterface = FindAgentInterface();
        if (agentInterface is null)
            return;

        var methods = agentInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (ExcludedMethodNames.Contains(method.Name))
                continue;

            // skip property accessors and special methods
            if (method.IsSpecialName)
                continue;

            // skip methods returning complex domain types — they aren't useful as LLM tools
            // and can cause recursive loops (e.g., FormatResponse returning RichOutput)
            if (!IsToolSafeReturnType(method.ReturnType))
                continue;

            try
            {
                tools.Add(AIFunctionFactory.Create(method, this));
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Skipping tool {Method} — incompatible signature", method.Name);
            }
        }
    }

    private Type? FindAgentInterface()
    {
        var agentInterfaces = GetType().GetInterfaces()
            .Where(IsAgentLeafInterface)
            .ToList();

        // prefer leaf: exclude any interface that is a base of another candidate
        return agentInterfaces
            .FirstOrDefault(i => !agentInterfaces.Any(other => other != i && i.IsAssignableFrom(other)))
            ?? agentInterfaces.FirstOrDefault();
    }

    private static bool IsAgentLeafInterface(Type iface)
    {
        if (iface == typeof(IAgent) || !typeof(IAgent).IsAssignableFrom(iface))
            return false;

        // exclude infrastructure communication interfaces
        if (iface.IsGenericType)
        {
            var def = iface.GetGenericTypeDefinition();
            if (def == typeof(IReceiver<>) || def == typeof(IStreamConsumer<>) || def == typeof(IStreamProducer<>))
                return false;
        }

        return true;
    }

    private static HashSet<string> BuildExcludedMethodNames()
    {
        var excluded = new HashSet<string>();

        foreach (var method in typeof(IAgent).GetMethods())
            excluded.Add(method.Name);

        foreach (var baseIface in typeof(IAgent).GetInterfaces())
            foreach (var method in baseIface.GetMethods())
                excluded.Add(method.Name);

        excluded.Add("GetTitle");

        return excluded;
    }

    private static bool IsToolSafeReturnType(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(void))
            return true;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            returnType = returnType.GetGenericArguments()[0];

        return IsSimpleType(returnType)
            || (returnType.IsArray && IsSimpleType(returnType.GetElementType()!));
    }

    private static bool IsSimpleType(Type type) =>
        type == typeof(string) || type.IsPrimitive || type == typeof(decimal) || type.IsEnum;

    protected static void RegisterToolMethods(List<AITool> tools, object toolSource)
    {
        var methods = toolSource.GetType().GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (method.GetCustomAttributes(typeof(DescriptionAttribute), false).Length > 0)
                tools.Add(AIFunctionFactory.Create(method, toolSource));
        }
    }

    private void RegisterSchedulingTools(List<AITool> tools)
    {
        var methods = typeof(Agent).GetMethods(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (method.GetCustomAttributes(typeof(DescriptionAttribute), false).Length > 0)
            {
                try
                {
                    tools.Add(AIFunctionFactory.Create(method, this));
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Skipping scheduling tool {Method} — incompatible signature", method.Name);
                }
            }
        }
    }
}