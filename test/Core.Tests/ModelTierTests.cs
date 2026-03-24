using Core.AI;
using Xunit;

namespace IAW.Core.Tests;

public class ModelTierTests
{
    [Fact]
    public void Fast_HasValidServiceKey()
    {
        var fast = LLMModel.All.First(m => m.GetType() == typeof(Fast));
        Assert.Equal("tier-tier-fast", fast.ServiceKey);
    }

    [Fact]
    public void Balanced_HasValidServiceKey()
    {
        var balanced = LLMModel.All.First(m => m.GetType() == typeof(Balanced));
        Assert.Equal("tier-tier-balanced", balanced.ServiceKey);
    }

    [Fact]
    public void Reasoning_HasValidServiceKey()
    {
        var reasoning = LLMModel.All.First(m => m.GetType() == typeof(Reasoning));
        Assert.Equal("tier-tier-reasoning", reasoning.ServiceKey);
    }

    [Fact]
    public void TierTypes_SurviveAutoDiscovery()
    {
        var all = LLMModel.All;
        Assert.Contains(all, m => m is Fast);
        Assert.Contains(all, m => m is Balanced);
        Assert.Contains(all, m => m is Reasoning);
    }

    [Fact]
    public void TierTypes_HaveTierProvider()
    {
        var tiers = LLMModel.All.Where(m => m.Provider == "tier").ToList();
        Assert.Equal(3, tiers.Count);
    }

    [Fact]
    public void TierTypes_DoNotInterfereWithConcreteModels()
    {
        var concreteCount = LLMModel.All.Count(m => m.Provider != "tier");
        Assert.True(concreteCount > 0);
    }
}