using System.Reflection;
using Core;
using Xunit;

namespace IAW.Agents.Tests;

public sealed class ArchitectureGuardTests
{
    [Fact]
    public void CoreAgent_DoesNotExposeLegacyChannelStreamingMethods()
    {
        var coreAssembly = typeof(IAgent).Assembly;
        var legacyAgentType = coreAssembly.GetType("Core.Agent", throwOnError: true, ignoreCase: false);
        Assert.NotNull(legacyAgentType);

        var methods = legacyAgentType!.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(methods, method =>
            method.Name == "PublishStreamAsync" &&
            method.GetParameters() is
            [
                { ParameterType: not null } p0,
                { ParameterType: not null } p1,
                { ParameterType: not null } p2
            ] &&
            p0.ParameterType == typeof(string) &&
            p1.ParameterType == typeof(string) &&
            p2.ParameterType == typeof(CancellationToken));

        Assert.DoesNotContain(methods, method =>
            method.Name == "SubscribeStreamAsync" &&
            method.GetParameters() is
            [
                { ParameterType: not null } p0,
                { ParameterType: not null } p1
            ] &&
            p0.ParameterType == typeof(string) &&
            p1.ParameterType == typeof(CancellationToken));

        Assert.DoesNotContain(methods, method =>
            method.Name == "GetStreamSubscriberCountsAsync");
    }

    [Fact]
    public void CoreAssembly_DoesNotContainLegacyChannelStreamingTypes()
    {
        var coreAssembly = typeof(IAgent).Assembly;

        Assert.Null(coreAssembly.GetType("Core.AgentStreamHub", throwOnError: false, ignoreCase: false));
        Assert.Null(coreAssembly.GetType("Core.AgentTopicChannel", throwOnError: false, ignoreCase: false));
        Assert.Null(coreAssembly.GetType("Core.AgentStreamSubscription", throwOnError: false, ignoreCase: false));
    }

    [Fact]
    public void CoreAssembly_AgentIsPublicAndExtendsDurableGrain()
    {
        var coreAssembly = typeof(IAgent).Assembly;
        var agentType = coreAssembly.GetType("Core.Agent", throwOnError: true, ignoreCase: false);

        Assert.NotNull(agentType);
        Assert.True(agentType!.IsPublic);
        Assert.True(typeof(IAgent).IsPublic);
        Assert.True(typeof(Orleans.Journaling.DurableGrain).IsAssignableFrom(agentType));

        var weatherAgentType = coreAssembly.GetType("Core.WeatherAgent", throwOnError: false, ignoreCase: false);
        Assert.Null(weatherAgentType);
    }
}
