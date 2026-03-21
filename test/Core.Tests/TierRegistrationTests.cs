using Core.AI;
using IAW.Testing;
using IAW.Agents.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IAW.Core.Tests;

public class TierRegistrationTests : AgentTest<ThreadAgent>
{
    [Fact]
    public void MockClient_RegisteredForTierServiceKeys()
    {
        var services = new ServiceCollection();
        var mockClient = new MockChatClient().ReturnsText("mock-response");
        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(services, mockClient);

        var provider = services.BuildServiceProvider();

        var fastKey = LLMModel.All.First(m => m is Fast).ServiceKey;
        var balancedKey = LLMModel.All.First(m => m is Balanced).ServiceKey;
        var reasoningKey = LLMModel.All.First(m => m is Reasoning).ServiceKey;

        var fastClient = provider.GetKeyedService<IChatClient>(fastKey);
        var balancedClient = provider.GetKeyedService<IChatClient>(balancedKey);
        var reasoningClient = provider.GetKeyedService<IChatClient>(reasoningKey);

        Assert.NotNull(fastClient);
        Assert.NotNull(balancedClient);
        Assert.NotNull(reasoningClient);
    }

    [Fact]
    public void LlmAttribute_ResolvesForTierTypes()
    {
        var fastAttr = new LlmAttribute<Fast>();
        var balancedAttr = new LlmAttribute<Balanced>();
        var reasoningAttr = new LlmAttribute<Reasoning>();

        Assert.Equal("tier-tier-fast", fastAttr.ServiceKey);
        Assert.Equal("tier-tier-balanced", balancedAttr.ServiceKey);
        Assert.Equal("tier-tier-reasoning", reasoningAttr.ServiceKey);
    }
}
