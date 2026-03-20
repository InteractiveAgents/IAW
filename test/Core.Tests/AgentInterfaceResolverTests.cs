using System.Runtime.CompilerServices;
using Core;
using Core.Contracts;
using IAW.Agents.Orchestration;
using Xunit;

namespace IAW.Core.Tests;

public class AgentInterfaceResolverTests
{
    public AgentInterfaceResolverTests()
    {
        // ensure the Agents assembly is loaded before scanning
        RuntimeHelpers.RunClassConstructor(typeof(IThread).TypeHandle);
    }

    [Fact]
    public void Resolve_KnownInterface_ReturnsType()
    {
        var allAgentInterfaces = AgentInterfaceResolver.DiscoverAgentInterfaces();
        Assert.NotEmpty(allAgentInterfaces);
    }

    [Fact]
    public void Resolve_ByExactName_ReturnsMatch()
    {
        var result = AgentInterfaceResolver.Resolve("IThread");
        Assert.NotNull(result);
        Assert.Equal("IThread", result.Name);
    }

    [Fact]
    public void Resolve_ByNameWithoutPrefix_ReturnsMatch()
    {
        var result = AgentInterfaceResolver.Resolve("Thread");
        Assert.NotNull(result);
        Assert.Equal("IThread", result.Name);
    }

    [Fact]
    public void Resolve_ByKebabCase_ReturnsMatch()
    {
        var result = AgentInterfaceResolver.Resolve("thread");
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNull()
    {
        var result = AgentInterfaceResolver.Resolve("INonExistent");
        Assert.Null(result);
    }
}
