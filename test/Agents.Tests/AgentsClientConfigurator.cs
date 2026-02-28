using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;

namespace IAW.Agents.Tests;

public sealed class AgentsClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams("agents");
    }
}
