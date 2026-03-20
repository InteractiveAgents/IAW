using Core.Contracts;
using Core.Registry;
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
            var contractInterface = GetAgentContractInterface(type);
            if (contractInterface is null) continue;

            var (_, description, _) = AgentInterfaceMetadata.ReadFrom(contractInterface);
            Assert.True(!string.IsNullOrEmpty(description), $"Agent {type.Name} missing AgentDescription");
        }
    }

    [Fact]
    public void AllAgents_HaveCapabilities()
    {
        var agentBaseType = typeof(IAW.Core.Agent);
        var agentTypes = GetProductionAgentTypes(agentBaseType);

        foreach (var type in agentTypes)
        {
            var contractInterface = GetAgentContractInterface(type);
            if (contractInterface is null) continue;

            var (_, _, capabilities) = AgentInterfaceMetadata.ReadFrom(contractInterface);
            Assert.True(capabilities.Length > 0, $"Agent {type.Name} missing AgentCapabilities");
        }
    }

    static Type? GetAgentContractInterface(Type agentType) =>
        agentType.GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && !i.IsGenericType);

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
