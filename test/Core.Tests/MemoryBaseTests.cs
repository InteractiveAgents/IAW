using System.Reflection;
using IAW.Core;
using Xunit;

namespace IAW.Core.Tests;

public class MemoryBaseTests
{
    [Fact]
    public void Memory_extends_Agent()
    {
        Assert.True(typeof(Memory).IsSubclassOf(typeof(Agent)));
    }

    [Fact]
    public void Memory_is_abstract()
    {
        Assert.True(typeof(Memory).IsAbstract);
    }

    [Fact]
    public void Memory_has_Observe_method()
    {
        var method = typeof(Memory).GetMethod("Observe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void Memory_has_Search_method()
    {
        var method = typeof(Memory).GetMethod("Search", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void Memory_has_Consolidate_method()
    {
        var method = typeof(Memory).GetMethod("Consolidate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void Memory_has_Decay_method()
    {
        var method = typeof(Memory).GetMethod("Decay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }

    [Fact]
    public void Memory_has_Forget_method()
    {
        var method = typeof(Memory).GetMethod("Forget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
    }
}
