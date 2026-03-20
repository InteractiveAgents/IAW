using System.Reflection;
using Core;
using Xunit;

namespace IAW.Core.Tests;

public class MemoryBaseTests
{
    [Fact]
    public void MemoryAgentBase_extends_AgentGeneric()
    {
        Assert.Equal(typeof(Agent<>), typeof(MemoryAgentBase<>).BaseType!.GetGenericTypeDefinition());
    }

    [Fact]
    public void MemoryAgentBase_is_abstract()
    {
        Assert.True(typeof(MemoryAgentBase<>).IsAbstract);
    }

    [Fact]
    public void MemoryAgentBase_has_Observe_method()
    {
        var method = typeof(MemoryAgentBase<>).GetMethod("Observe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void MemoryAgentBase_has_Search_method()
    {
        var method = typeof(MemoryAgentBase<>).GetMethod("Search", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void MemoryAgentBase_has_Consolidate_method()
    {
        var method = typeof(MemoryAgentBase<>).GetMethod("Consolidate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void MemoryAgentBase_has_Decay_method()
    {
        var method = typeof(MemoryAgentBase<>).GetMethod("Decay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void MemoryAgentBase_has_Forget_method()
    {
        var method = typeof(MemoryAgentBase<>).GetMethod("Forget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }
}
