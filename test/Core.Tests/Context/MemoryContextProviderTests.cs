using Core.Context;
using Xunit;

namespace IAW.Core.Tests.Context;

public class MemoryContextProviderTests
{
    [Fact]
    public void MemoryContextProvider_has_correct_name()
    {
        var provider = new MemoryContextProvider([]);
        Assert.Equal("Memory", provider.Name);
    }

    [Fact]
    public void MemoryContextProvider_implements_IAgentContextProvider()
    {
        Assert.True(typeof(IAgentContextProvider).IsAssignableFrom(typeof(MemoryContextProvider)));
    }

    [Fact]
    public async Task MemoryContextProvider_with_no_agents_returns_empty()
    {
        var provider = new MemoryContextProvider([]);
        var context = await provider.GetContextAsync("test-agent", "test query", TestContext.Current.CancellationToken);
        Assert.Empty(context);
    }
}