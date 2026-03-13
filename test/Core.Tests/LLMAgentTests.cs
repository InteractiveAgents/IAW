using Core;
using Xunit;

namespace IAW.Core.Tests;

public class LLMAgentTests
{
    [Fact]
    public void LLM_extends_Agent()
    {
        Assert.True(typeof(LLM).IsSubclassOf(typeof(Agent)));
    }

    [Fact]
    public void LLM_is_abstract()
    {
        Assert.True(typeof(LLM).IsAbstract);
    }
}
