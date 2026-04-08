using Core.Communication;
using Core.Contracts;
using Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Reflection;

namespace IAW.Core;

public abstract partial class Agent
{
    private IReadOnlyList<AITool>? _cachedTools;

    private static readonly HashSet<string> ExcludedMethodNames = BuildExcludedMethodNames();

    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    protected virtual IReadOnlyList<AITool> DefineAdditionalTools() => [];

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

        DiscoverInterfaceTools(tools);

        tools.AddRange(DefineTools());
        tools.AddRange(DefineAdditionalTools());

        _cachedTools = tools;
        return _cachedTools;
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