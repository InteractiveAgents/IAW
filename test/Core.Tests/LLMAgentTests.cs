using Core;
using Xunit;

namespace IAW.Core.Tests;

public class LLMAgentTests
{
    [Fact]
    public void LlmAgentBase_extends_Agent()
    {
        Assert.True(typeof(LlmAgentBase<>).BaseType!.GetGenericTypeDefinition() == typeof(Agent<>));
    }

    [Fact]
    public void LlmAgentBase_is_abstract()
    {
        Assert.True(typeof(LlmAgentBase<>).IsAbstract);
    }
}
