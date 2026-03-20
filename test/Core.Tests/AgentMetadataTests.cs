using System.Reflection;
using Xunit;

namespace IAW.Core.Tests;

public class AgentMetadataTests
{
    [Fact]
    public void AllAgents_HaveDescription()
    {
        var agentBaseType = typeof(IAW.Core.Agent);
        var agentTypes = GetProductionAgentTypes(agentBaseType);

        foreach (var type in agentTypes)
        {
            var prop = type.GetProperty("AgentDescription", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            Assert.True(prop is not null, $"Agent {type.Name} missing AgentDescription");
        }
    }

    [Fact]
    public void AllAgents_HaveCapabilities()
    {
        var agentBaseType = typeof(IAW.Core.Agent);
        var agentTypes = GetProductionAgentTypes(agentBaseType);

        foreach (var type in agentTypes)
        {
            var prop = type.GetProperty("AgentCapabilities", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            Assert.True(prop is not null, $"Agent {type.Name} missing AgentCapabilities");
        }
    }

    static IEnumerable<Type> GetProductionAgentTypes(Type agentBaseType)
    {
        var testAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IAW.Core.Tests", "IAW.Testing", "xunit.v3.core", "xunit.v3.runner"
        };

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !testAssemblyNames.Contains(a.GetName().Name ?? ""))
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsSubclassOf(agentBaseType) && !t.IsAbstract);
    }
}
