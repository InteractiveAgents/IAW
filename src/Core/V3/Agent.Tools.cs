using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.AI;
using Core.V3.Tools;

namespace Core.V3;

public abstract partial class Agent
{
    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    private IReadOnlyList<AITool> GetAllTools()
    {
        var tools = new List<AITool>();

        var workspaceTools = new WorkspaceTools(
            () => GetWorkspacePath() ?? ".",
            path => state[WorkspacePathKey] = new StateEntry(WorkspacePathKey, path));
        RegisterToolMethods(tools, workspaceTools);

        var workspacePath = GetWorkspacePath();
        if (workspacePath is not null)
        {
            RegisterToolMethods(tools, new FileTools(() => workspacePath));
            RegisterToolMethods(tools, new ShellTools(() => workspacePath));
        }

        RegisterToolMethods(tools, new WebTools(new HttpClient()));

        tools.AddRange(DefineTools());
        return tools;
    }

    private static void RegisterToolMethods(List<AITool> tools, object toolSource)
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
