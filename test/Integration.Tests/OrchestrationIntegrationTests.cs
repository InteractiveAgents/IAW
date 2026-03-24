using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Integration.Tests;

public class OrchestrationIntegrationTests : AgentTest<CodeOrchestratorAgent>
{
    [Fact]
    public async Task CodeOrchestrator_responds_to_prompt()
    {
        var ct = TestContext.Current.CancellationToken;
        var orchestrator = Agent("code-orchestrator");
        var response = await orchestrator.GetResponse("Hello", ct);
        Assert.NotNull(response);
    }
}