using Core;
using Core.Communication;
using Core.Contracts;
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

    protected virtual bool DiscoverInterfaceToolsEnabled => true;

    protected IReadOnlyList<UIPart> DrainPendingUIHints()
    {
        if (_pendingUIHints.Count == 0) return Array.Empty<UIPart>();
        var copy = _pendingUIHints.ToArray();
        _pendingUIHints.Clear();
        return copy;
    }

    protected void ClearPendingUIHints() => _pendingUIHints.Clear();

    protected void AddPendingUIHint(UIPart part) => _pendingUIHints.Add(part);

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

        _cachedTools = tools;
        return _cachedTools;
    }

    protected AITool CreateProposeOptionsTool()
    {
        var proposeMethod = typeof(Agent).GetMethod(
            nameof(ProposeOptions),
            BindingFlags.NonPublic | BindingFlags.Instance);
        return AIFunctionFactory.Create(proposeMethod!, this);
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
