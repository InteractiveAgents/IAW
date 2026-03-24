using Core.Context;
using Xunit;

namespace IAW.Core.Tests.Context;

public class UserContextProviderTests
{
    [Fact]
    public void Has_correct_name()
    {
        var provider = new UserContextProvider(null!);
        Assert.Equal("user-profile", provider.Name);
    }

    [Fact]
    public void Implements_IAgentContextProvider()
    {
        Assert.True(typeof(IAgentContextProvider).IsAssignableFrom(typeof(UserContextProvider)));
    }

    [Fact]
    public async Task Returns_empty_on_error()
    {
        var provider = new UserContextProvider(null!);
        var result = await provider.GetContextAsync("test-agent", "some query", TestContext.Current.CancellationToken);
        Assert.Empty(result);
    }
}