using Core.Context;
using Xunit;

namespace IAW.Core.Tests.Context;

public class RAGContextProviderTests
{
    [Fact]
    public void Has_correct_name()
    {
        var provider = new RAGContextProvider(null!, null!);
        Assert.Equal("document-search", provider.Name);
    }

    [Fact]
    public void Implements_IAgentContextProvider()
    {
        Assert.True(typeof(IAgentContextProvider).IsAssignableFrom(typeof(RAGContextProvider)));
    }

    [Fact]
    public async Task Returns_empty_when_qdrant_unavailable()
    {
        // null dependencies will throw, caught by try/catch — returns empty
        var provider = new RAGContextProvider(null!, null!);
        var result = await provider.GetContextAsync("test-agent", "some query", TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }
}
