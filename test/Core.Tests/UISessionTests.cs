using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests;

public sealed class UISessionTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .UseInMemoryReminderService();

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        siloBuilder.Services.AddSingleton<IAttributeToFactoryMapper<UISessionStateAttribute>, UISessionStateMapper>();
    }
}

public class UISessionTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<UISessionTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task RegisterApproval_And_ResolveApproval_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("user-1");
        await session.RegisterApproval("ap1", "Deploy to prod?", ["yes", "no"], "my-project", ct);
        var result = await session.ResolveApproval("ap1", "yes", ct);
        Assert.Equal("ap1", result.ApprovalId);
        Assert.Equal("yes", result.Decision);
    }

    [Fact]
    public async Task HandleCallback_RoutesApproval()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("user-2");
        await session.RegisterApproval("ap2", "Merge PR?", ["approve", "decline"], "proj", ct);
        var result = await session.HandleCallback("ap2", "ap:ap2:approve", ct);
        Assert.Equal("approve", result.Action);
    }

    [Fact]
    public async Task HasPendingFreeTextInput_ReturnsFalseByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("user-3");
        var pending = await session.HasPendingFreeTextInput("topic-1", ct);
        Assert.False(pending);
    }
}
