using Core.Contracts;

namespace Core.Registry;

public static class AgentInterfaceMetadata
{
    public static string DisplayName<T>() where T : IAgent => T.AgentDisplayName;
    public static string Description<T>() where T : IAgent => T.AgentDescription;
    public static string[] Capabilities<T>() where T : IAgent => T.AgentCapabilities;
    public static string Instructions<T>() where T : IAgent => T.AgentInstructions;
    public static string[] RoutingExamples<T>() where T : IAgent => T.AgentRoutingExamples;

    public static (string DisplayName, string Description, string[] Capabilities, string[] RoutingExamples) ReadFrom(Type agentInterfaceType)
    {
        var displayName = (string)typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(DisplayName))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        var description = (string)typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(Description))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        var capabilities = (string[])typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(Capabilities))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        var routingExamples = (string[])typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(RoutingExamples))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        return (displayName, description, capabilities, routingExamples);
    }
}