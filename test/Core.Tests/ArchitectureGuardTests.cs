using System.Reflection;
using IAW.Core;
using IAW.Core.Communication;
using IAW.Core.Messages;
using Xunit;

namespace IAW.Core.Tests;

public class ArchitectureGuardTests
{
    private static readonly Assembly V3Assembly = typeof(Agent).Assembly;

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
        var messageTypes = V3Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.V3.Messages" && !t.IsInterface && !t.IsAbstract);

        Assert.NotEmpty(messageTypes);
        foreach (var type in messageTypes)
            Assert.True(typeof(IAgentMessage).IsAssignableFrom(type), $"{type.Name} should implement IAgentMessage");
    }

    [Fact]
    public void AllEventTypes_ImplementIEvent()
    {
        var eventTypes = V3Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.V3.Messages" && t.Name.EndsWith("Event") && !t.IsInterface);

        Assert.NotEmpty(eventTypes);
        foreach (var type in eventTypes)
            Assert.True(typeof(IEvent).IsAssignableFrom(type), $"{type.Name} should implement IEvent");
    }

    [Fact]
    public void AllCommandTypes_ImplementICommand()
    {
        var commandTypes = V3Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.V3.Messages" && t.Name.EndsWith("Command") && !t.IsInterface);

        Assert.NotEmpty(commandTypes);
        foreach (var type in commandTypes)
            Assert.True(typeof(ICommand).IsAssignableFrom(type), $"{type.Name} should implement ICommand");
    }

    [Fact]
    public void AllNotificationTypes_ImplementINotification()
    {
        var notifTypes = V3Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.V3.Messages" && t.Name.EndsWith("Notification") && !t.IsInterface);

        Assert.NotEmpty(notifTypes);
        foreach (var type in notifTypes)
            Assert.True(typeof(INotification).IsAssignableFrom(type), $"{type.Name} should implement INotification");
    }

    [Fact]
    public void AllSerializableTypes_HaveGenerateSerializerAttribute()
    {
        var serializableTypes = V3Assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Core.V3"))
            .Where(t => !t.IsInterface && !t.IsAbstract && !t.IsEnum)
            .Where(t => t.GetCustomAttribute<GenerateSerializerAttribute>() is not null);

        Assert.NotEmpty(serializableTypes);

        // All record types in Messages namespace must have it
        var messageRecords = V3Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.V3.Messages" && !t.IsInterface && !t.IsAbstract);

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
    public void IBroadcaster_GenericConstraint_RequiresIAgentMessage()
    {
        var constraint = typeof(IBroadcaster<>).GetGenericArguments()[0].GetGenericParameterConstraints();
        Assert.Contains(typeof(IAgentMessage), constraint);
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
    public void NoV3SourceFiles_ContainXmlDocSummary()
    {
        var v3Root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "Core", "V3");
        v3Root = Path.GetFullPath(v3Root);

        if (!Directory.Exists(v3Root))
        {
            // Running from a different location; skip gracefully
            return;
        }

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(v3Root, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("/// <summary>"))
                    violations.Add($"{Path.GetRelativePath(v3Root, file)}:{i + 1}");
            }
        }

        Assert.True(violations.Count == 0, $"XML doc comments found in:\n{string.Join("\n", violations)}");
    }
}
