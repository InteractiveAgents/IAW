using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using System.Collections.Generic;

namespace Core.V3;

public abstract partial class Agent
{
    protected virtual IReadOnlyList<AITool> DefineTools() => [];

    private IReadOnlyList<AITool> GetAllTools()
    {
        // Stub core tools - full port Workspace/File/Shell/Web/Tracking next
        var coreTools = new List<AITool>();
        var subclassTools = DefineTools();
        return [.. coreTools, .. subclassTools];
    }
}
