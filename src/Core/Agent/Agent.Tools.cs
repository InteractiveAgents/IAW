using System.ComponentModel;
using System.Reflection;
using Core.Contracts;
using Core.Tools;
using Microsoft.Extensions.AI;

namespace IAW.Core;

public abstract partial class Agent
{
    private IReadOnlyList<AITool>? _cachedTools;

    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    private IReadOnlyList<AITool> GetAllTools()
    {
        if (_cachedTools is not null)
            return _cachedTools;

        var tools = new List<AITool>();

        var workspaceTools = new WorkspaceTools(
            () => GetWorkspacePath() ?? ".",
            path =>
            {
                state[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path);
                _cachedTools = null;
            });
        RegisterToolMethods(tools, workspaceTools);

        var workspacePath = GetWorkspacePath();
        if (workspacePath is not null)
        {
            RegisterToolMethods(tools, new FileTools(() => workspacePath));
            RegisterToolMethods(tools, new ShellTools(() => workspacePath));
        }

        RegisterToolMethods(tools, new WebTools(new HttpClient()));

        tools.AddRange(DefineTools());
        _cachedTools = tools;
        return _cachedTools;
    }

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
}
