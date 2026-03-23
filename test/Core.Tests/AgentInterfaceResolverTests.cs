using System.Runtime.CompilerServices;
using Core;
using Core.Contracts;
using IAW.Agents.System;
using IAW.Agents.Coding;
using IAW.Agents.Orchestration;
using Xunit;

namespace IAW.Core.Tests;

public class AgentInterfaceResolverTests
{
    public AgentInterfaceResolverTests()
    {
        RuntimeHelpers.RunClassConstructor(typeof(IThread).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(IShell).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(IDotNet).TypeHandle);
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

    [Fact]
    public void ResolveByDisplayName_FindsShellAgent()
    {
        var result = AgentInterfaceResolver.ResolveByDisplayName("Shell");
        Assert.NotNull(result);
        Assert.Equal("IShell", result!.Name);
    }

    [Fact]
    public void ResolveByDisplayName_CaseInsensitive()
    {
        var result = AgentInterfaceResolver.ResolveByDisplayName("dotnet");
        Assert.NotNull(result);
        Assert.Equal("IDotNet", result!.Name);
    }

    [Fact]
    public void ResolveByDisplayName_ReturnsNull_ForUnknown()
    {
        var result = AgentInterfaceResolver.ResolveByDisplayName("NonExistentAgent");
        Assert.Null(result);
    }
}
