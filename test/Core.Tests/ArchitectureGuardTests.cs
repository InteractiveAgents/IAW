using System.Reflection;
using IAW.Core;
using Core.Communication;
using Core.Contracts;
using Core.Messages;
using Xunit;

namespace IAW.Core.Tests;

public class ArchitectureGuardTests
{
    private static readonly Assembly CoreAssembly = typeof(Agent).Assembly;

    [Fact]
    public void Agent_ExtendsDurableGrain()
    {
        var baseType = typeof(Agent).BaseType;
        Assert.NotNull(baseType);
        Assert.Equal("DurableGrain", baseType!.Name);
    }

    [Fact]
    public void Agent_IsAbstract()
    {
        Assert.True(typeof(Agent).IsAbstract);
    }

    [Fact]
    public void Agent_ImplementsIAgent()
    {
        Assert.True(typeof(IAgent).IsAssignableFrom(typeof(Agent)));
    }

    [Fact]
    public void IAgent_ExtendsIGrainWithStringKey()
    {
        Assert.True(typeof(IGrainWithStringKey).IsAssignableFrom(typeof(IAgent)));
    }

    [Fact]
    public void AllMessageTypes_ImplementIAgentMessage()
    {
        var messageTypes = CoreAssembly.GetTypes()
            .Where(t => t.Namespace == "Core.Messages" && !t.IsInterface && !t.IsAbstract);

        Assert.NotEmpty(messageTypes);
        foreach (var type in messageTypes)
            Assert.True(typeof(IAgentMessage).IsAssignableFrom(type), $"{type.Name} should implement IAgentMessage");
    }

    [Fact]
    public void AllEventTypes_ImplementIEvent()
    {
        var eventTypes = CoreAssembly.GetTypes()
            .Where(t => t.Namespace == "Core.Messages" && t.Name.EndsWith("Event") && !t.IsInterface);

        Assert.NotEmpty(eventTypes);
        foreach (var type in eventTypes)
            Assert.True(typeof(IEvent).IsAssignableFrom(type), $"{type.Name} should implement IEvent");
    }

    [Fact]
    public void AllCommandTypes_ImplementICommand()
    {
        var commandTypes = CoreAssembly.GetTypes()
            .Where(t => t.Namespace == "Core.Messages" && t.Name.EndsWith("Command") && !t.IsInterface);

        Assert.NotEmpty(commandTypes);
        foreach (var type in commandTypes)
            Assert.True(typeof(ICommand).IsAssignableFrom(type), $"{type.Name} should implement ICommand");
    }

    [Fact]
    public void AllSerializableTypes_HaveGenerateSerializerAttribute()
    {
        var serializableTypes = CoreAssembly.GetTypes()
            .Where(t => t.Namespace is not null && (t.Namespace.StartsWith("Core.") || t.Namespace == "IAW.Core"))
            .Where(t => !t.IsInterface && !t.IsAbstract && !t.IsEnum)
            .Where(t => t.GetCustomAttribute<GenerateSerializerAttribute>() is not null);

        Assert.NotEmpty(serializableTypes);

        var messageRecords = CoreAssembly.GetTypes()
            .Where(t => t.Namespace == "Core.Messages" && !t.IsInterface && !t.IsAbstract);

        foreach (var type in messageRecords)
            Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>());
    }

    [Fact]
    public void IStreamConsumer_GenericConstraint_RequiresIEvent()
    {
        var constraint = typeof(IStreamConsumer<>).GetGenericArguments()[0].GetGenericParameterConstraints();
        Assert.Contains(typeof(IEvent), constraint);
    }

    [Fact]
    public void IStreamProducer_GenericConstraint_RequiresIEvent()
    {
        var constraint = typeof(IStreamProducer<>).GetGenericArguments()[0].GetGenericParameterConstraints();
        Assert.Contains(typeof(IEvent), constraint);
    }

    [Fact]
    public void DynamicAgent_IsNotAbstract()
    {
        Assert.False(typeof(DynamicAgent).IsAbstract);
    }

    [Fact]
    public void DynamicAgent_ImplementsIDynamicAgent()
    {
        Assert.True(typeof(IDynamicAgent).IsAssignableFrom(typeof(DynamicAgent)));
    }

    [Fact]
    public void NoCoreSourceFiles_ContainXmlDocSummary()
    {
        var coreRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "Core");
        coreRoot = Path.GetFullPath(coreRoot);

        if (!Directory.Exists(coreRoot))
            return;

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("/// <summary>"))
                    violations.Add($"{Path.GetRelativePath(coreRoot, file)}:{i + 1}");
            }
        }

        Assert.True(violations.Count == 0, $"XML doc comments found in:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void AllAgentsInIAWAgents_ExtendAgent()
    {
        var agentsAssembly = typeof(IAW.Agents.Infrastructure.FileSystemAgent).Assembly;
        var concreteGrains = agentsAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                && t.Name.EndsWith("Agent")
                && !t.Name.StartsWith("Proxy_")
                && typeof(IGrain).IsAssignableFrom(t));

        Assert.NotEmpty(concreteGrains);
        foreach (var type in concreteGrains)
            Assert.True(typeof(Agent).IsAssignableFrom(type), $"{type.FullName} should extend Agent");
    }

    [Fact]
    public void NoV1OrV2TypesExist()
    {
        var allAssemblies = new[]
        {
            typeof(Agent).Assembly,
            typeof(IAW.Agents.Infrastructure.FileSystemAgent).Assembly
        };

        foreach (var assembly in allAssemblies)
        {
            var legacyTypes = assembly.GetTypes()
                .Where(t => t.Namespace is not null
                    && (t.Namespace.Contains(".V1") || t.Namespace.Contains(".V2")));

            Assert.Empty(legacyTypes);
        }
    }
}
