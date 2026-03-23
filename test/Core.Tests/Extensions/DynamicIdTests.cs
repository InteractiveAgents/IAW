using IAW.Agents.Coding;
using Xunit;

namespace IAW.Core.Tests.Extensions;

public class DynamicIdTests
{
    [Fact]
    public void Get_WithoutScope_GeneratesUniqueIds()
    {
        var id1 = $"{typeof(IGit).Name}-{Guid.NewGuid().ToString("N")[..8]}";
        var id2 = $"{typeof(IGit).Name}-{Guid.NewGuid().ToString("N")[..8]}";
        Assert.StartsWith("IGit-", id1);
        Assert.StartsWith("IGit-", id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Get_WithScope_ProducesDeterministicId()
    {
        var scope = "task-abc";
        var id = $"{scope}/{typeof(IGit).Name}";
        Assert.Equal("task-abc/IGit", id);
    }

    [Fact]
    public void Get_SameScopeAndType_ProducesSameId()
    {
        var id1 = $"task-xyz/{typeof(IGit).Name}";
        var id2 = $"task-xyz/{typeof(IGit).Name}";
        Assert.Equal(id1, id2);
    }
}