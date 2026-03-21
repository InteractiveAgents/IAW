using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class OptionsTestSiloConfigurator : ISiloConfigurator
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

public class OptionsTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<OptionsTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task RegisterOptions_And_HandleCallback_ResolvesSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("opt-user-1");

        var options = new[]
        {
            new PendingOption("Joke A", "a"),
            new PendingOption("Joke B", "b"),
            new PendingOption("Joke C", "c")
        };
        await session.RegisterOptions("opt-abc123", "Which joke?", options, "proj/slug", "option", ct);

        var result = await session.HandleCallback("opt-abc123", "opt:opt-abc123:b", ct);

        Assert.Contains("Joke B", result.NewText);
        Assert.Equal("b", result.Action);
        Assert.Null(result.Buttons);
    }

    [Fact]
    public async Task HandleCallback_UnknownOptionsId_ReturnsUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("opt-user-2");

        var result = await session.HandleCallback("x", "opt:nonexistent:a", ct);

        Assert.Equal("Unknown callback", result.Toast);
    }

    [Fact]
    public async Task RegisterOptions_SecondCall_OverwritesPrevious()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("opt-user-3");

        var first = new[] { new PendingOption("A", "a") };
        var second = new[] { new PendingOption("X", "x"), new PendingOption("Y", "y") };

        await session.RegisterOptions("opt-1", "First?", first, "proj", "option", ct);
        await session.RegisterOptions("opt-1", "Second?", second, "proj", "option", ct);

        var result = await session.HandleCallback("opt-1", "opt:opt-1:y", ct);
        Assert.Contains("Y", result.NewText);
    }
}
