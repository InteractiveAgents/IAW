using Core.AI;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests;

public sealed class UserProfileTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore");

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        siloBuilder.Services.AddSingleton<IAttributeToFactoryMapper<UserProfileStateAttribute>, UserProfileStateMapper>();
    }
}

public class UserProfileTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<UserProfileTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUserProfile Profile(string id) => _cluster.Client.GetGrain<IUserProfile>(id);

    [Fact]
    public async Task RegisterProject_And_ResolveProject_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-1");
        await profile.RegisterProject("my-app", "topic-42", ct);
        var slug = await profile.ResolveProject("topic-42", ct);
        Assert.Equal("my-app", slug);
    }

    [Fact]
    public async Task GetProjects_ReturnsRegisteredProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-2");
        await profile.RegisterProject("alpha", "topic-1", ct);
        await profile.RegisterProject("beta", "topic-2", ct);
        var projects = await profile.GetProjects(ct);
        Assert.Equal(2, projects.Count);
    }

    [Fact]
    public async Task RemoveProject_DeletesMapping()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-3");
        await profile.RegisterProject("temp", "topic-99", ct);
        await profile.RemoveProject("temp", ct);
        var slug = await profile.ResolveProject("topic-99", ct);
        Assert.Null(slug);
    }

    [Fact]
    public async Task SetPreference_And_GetPreferences_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-4");
        await profile.SetPreference("timezone", "Europe/Berlin", ct);
        var prefs = await profile.GetPreferences(ct);
        Assert.Equal("Europe/Berlin", prefs["timezone"]);
    }

    [Fact]
    public async Task RememberFact_And_RecallFacts_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-5");
        await profile.RememberFact("Prefers dark mode", ct);
        await profile.RememberFact("Uses C# daily", ct);
        var facts = await profile.RecallFacts("dark", ct);
        Assert.Single(facts);
        Assert.Equal("Prefers dark mode", facts[0]);
    }

    [Fact]
    public async Task GetPreferences_InitiallyEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-6");
        var prefs = await profile.GetPreferences(ct);
        Assert.Empty(prefs);
    }

    [Fact]
    public async Task GetProjects_InitiallyEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile("test-user-7");
        var projects = await profile.GetProjects(ct);
        Assert.Empty(projects);
    }
}
