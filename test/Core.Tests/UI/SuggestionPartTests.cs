using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class SuggestionTestSiloConfigurator : ISiloConfigurator
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

public class SuggestionPartTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SuggestionTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task RegisterOptions_WithSuggestionType_HandleCallback_ReturnsTypeInAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("sug-user-1");

        var options = new[]
        {
            new PendingOption("Another 5 rounds", "1"),
            new PendingOption("Final pick", "2")
        };
        await session.RegisterOptions("sug-abc123", "What next?", options, "proj/slug", "suggestion", ct);

        var result = await session.HandleCallback("sug-abc123", "opt:sug-abc123:1", ct);

        Assert.Contains("Another 5 rounds", result.NewText);
        Assert.Equal("suggestion:1", result.Action);
    }

    [Fact]
    public async Task RegisterOptions_WithOptionType_HandleCallback_ReturnsPlainAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("sug-user-2");

        var options = new[] { new PendingOption("Joke A", "1") };
        await session.RegisterOptions("opt-xyz", "Pick one", options, "proj", "option", ct);

        var result = await session.HandleCallback("opt-xyz", "opt:opt-xyz:1", ct);

        Assert.Equal("1", result.Action);
        Assert.DoesNotContain("suggestion:", result.Action ?? "");
    }
}
