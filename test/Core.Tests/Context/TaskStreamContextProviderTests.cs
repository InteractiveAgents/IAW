using Core.Context;
using Xunit;

namespace IAW.Core.Tests.Context;

public class TaskStreamContextProviderTests
{
    [Fact]
    public void TaskStreamContextProvider_has_correct_name()
    {
        var provider = new TaskStreamContextProvider(null!);
        Assert.Equal("TaskStream", provider.Name);
    }

    [Fact]
    public void TaskStreamContextProvider_implements_IAgentContextProvider()
    {
        Assert.True(typeof(IAgentContextProvider).IsAssignableFrom(typeof(TaskStreamContextProvider)));
    }
}