using System.Reflection;
using Core;
using Core.Contracts;
using Core.Messages;
using Core.Messages.Events;
using Core.Models;
using Core.Orchestration;
using IAW.Agents.Orchestration;
using Xunit;

namespace IAW.Core.Tests;

public class ArchitectureGuardV2Tests
{
    private static readonly Assembly AgentsAssembly = typeof(CodeOrchestratorAgent).Assembly;

    [Fact]
    public void All_stream_event_types_implement_IEvent()
    {
        var eventTypes = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Event") && t.IsClass && !t.IsAbstract
                && t.Namespace is not null
                && t.Namespace.StartsWith("Core.Messages"));

        Assert.NotEmpty(eventTypes);
        Assert.All(eventTypes, t => Assert.True(t.IsAssignableTo(typeof(IEvent)),
            $"{t.Name} should implement IEvent"));
    }

    [Fact]
    public void LLM_agents_extend_LLM_base()
    {
        var llmAgents = AgentsAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(LlmAgentBase)) && !t.IsAbstract);

        Assert.NotEmpty(llmAgents);
    }

    [Fact]
    public void Memory_agents_extend_Memory_base()
    {
        var memoryAgents = AgentsAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(MemoryAgentBase)) && !t.IsAbstract);

        Assert.NotEmpty(memoryAgents);
    }

    [Fact]
    public void Memory_entries_have_provenance_source_field()
    {
        var props = typeof(MemoryEntry).GetProperties();
        Assert.Contains(props, p => p.Name == "Source" && p.PropertyType == typeof(MemoryProvenance));
    }

    [Fact]
    public void All_task_stream_events_have_TaskId()
    {
        var taskEventTypes = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ITaskStreamEvent)) && !t.IsInterface);

        Assert.NotEmpty(taskEventTypes);
        Assert.All(taskEventTypes, t =>
        {
            var prop = t.GetProperty("TaskId");
            Assert.NotNull(prop);
        });
    }

    [Fact]
    public void IAgent_does_not_contain_HandleEvent()
    {
        var methods = typeof(IAgent).GetMethods();
        Assert.DoesNotContain(methods, m => m.Name == "HandleEvent");
    }

    [Fact]
    public void InterfaceCatalog_discovers_LLM_agents()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IOpus46");
        Assert.Contains(catalog, e => e.InterfaceName == "ISonnet46");
    }

    [Fact]
    public void InterfaceCatalog_discovers_Memory_agents()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IUserMemory");
        Assert.Contains(catalog, e => e.InterfaceName == "IProjectMemory");
    }

    [Fact]
    public void All_agents_have_matching_IAgent_derived_interfaces()
    {
        var agentTypes = AgentsAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Agent)) && !t.IsAbstract
                && !t.Name.StartsWith("Proxy_"));

        Assert.NotEmpty(agentTypes);
        foreach (var agent in agentTypes)
        {
            var hasSpecificInterface = agent.GetInterfaces()
                .Any(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i));
            Assert.True(hasSpecificInterface, $"{agent.FullName} should implement a specific IAgent-derived interface");
        }
    }
}
