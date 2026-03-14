using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class PaginatorTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore");
        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();
        siloBuilder.Services.AddSingleton<IAttributeToFactoryMapper<UISessionStateAttribute>, UISessionStateMapper>();
    }
}

public class PaginatorTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<PaginatorTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task StartPaginator_ReturnsInitialState()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-1");
        var items = new[] { "A", "B", "C", "D", "E" };

        var result = await session.StartPaginator("p1", items, 2, "test-project", ct);

        Assert.Equal("p1", result.Id);
        Assert.Equal(0, result.CurrentPage);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task StartPaginator_Idempotent_ReturnsExisting()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-2");
        var items = new[] { "A", "B", "C" };

        await session.StartPaginator("p2", items, 2, "proj", ct);
        await session.NavigatePaginator("p2", "next", ct);

        var second = await session.StartPaginator("p2", items, 2, "proj", ct);
        Assert.Equal(1, second.CurrentPage);
    }

    [Fact]
    public async Task NavigatePaginator_NextIncrementsPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-3");
        var items = new[] { "A", "B", "C", "D", "E" };
        await session.StartPaginator("p3", items, 2, "proj", ct);

        var result = await session.NavigatePaginator("p3", "next", ct);

        Assert.Equal(1, result.CurrentPage);
    }

    [Fact]
    public async Task NavigatePaginator_PrevDecrementsPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-4");
        var items = new[] { "A", "B", "C", "D", "E" };
        await session.StartPaginator("p4", items, 2, "proj", ct);
        await session.NavigatePaginator("p4", "next", ct);

        var result = await session.NavigatePaginator("p4", "prev", ct);

        Assert.Equal(0, result.CurrentPage);
    }

    [Fact]
    public async Task NavigatePaginator_NextClampedToMaxPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-5");
        var items = new[] { "A", "B", "C" };
        await session.StartPaginator("p5", items, 2, "proj", ct);

        // max page is 1 (ceil(3/2)-1)
        await session.NavigatePaginator("p5", "next", ct);
        var result = await session.NavigatePaginator("p5", "next", ct);

        Assert.Equal(1, result.CurrentPage);
    }

    [Fact]
    public async Task NavigatePaginator_PrevClampedToZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-6");
        var items = new[] { "A", "B", "C" };
        await session.StartPaginator("p6", items, 2, "proj", ct);

        var result = await session.NavigatePaginator("p6", "prev", ct);

        Assert.Equal(0, result.CurrentPage);
    }

    [Fact]
    public async Task NavigatePaginator_NonExistent_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-7");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => session.NavigatePaginator("nonexistent", "next", ct));
    }

    [Fact]
    public async Task HandleCallback_RoutesPaginatorNext()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-8");
        var items = new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" };
        await session.StartPaginator("p8", items, 2, "proj", ct);

        var result = await session.HandleCallback("cb1", "pg:p8:next", ct);

        Assert.NotNull(result.NewText);
        Assert.Contains("Charlie", result.NewText);
        Assert.Contains("Delta", result.NewText);
        Assert.Contains("Page 2/3", result.NewText);
    }

    [Fact]
    public async Task HandleCallback_PaginatorFirstPage_NoPrevButton()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-9");
        var items = new[] { "A", "B", "C", "D" };
        await session.StartPaginator("p9", items, 2, "proj", ct);

        // navigate to next and back to first
        await session.HandleCallback("cb1", "pg:p9:next", ct);
        var result = await session.HandleCallback("cb2", "pg:p9:prev", ct);

        Assert.NotNull(result.Buttons);
        Assert.DoesNotContain(result.Buttons, b => b.CallbackData.Contains("prev"));
        Assert.Contains(result.Buttons, b => b.CallbackData.Contains("next"));
    }

    [Fact]
    public async Task HandleCallback_PaginatorLastPage_NoNextButton()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-10");
        var items = new[] { "A", "B", "C" };
        await session.StartPaginator("p10", items, 2, "proj", ct);

        var result = await session.HandleCallback("cb1", "pg:p10:next", ct);

        Assert.NotNull(result.Buttons);
        Assert.Contains(result.Buttons, b => b.CallbackData.Contains("prev"));
        Assert.DoesNotContain(result.Buttons, b => b.CallbackData.Contains("next"));
    }

    [Fact]
    public async Task NavigatePaginator_SingleItemPage_StaysOnPageZero()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("pg-11");
        var items = new[] { "Only" };
        await session.StartPaginator("p11", items, 5, "proj", ct);

        var result = await session.NavigatePaginator("p11", "next", ct);

        Assert.Equal(0, result.CurrentPage);
    }
}

public class PaginatorStateUnitTests
{
    [Fact]
    public void PaginatorState_DefaultPage_IsZero()
    {
        var paginatorState = new PaginatorState
        {
            Id = "p1",
            Items = new List<string> { "A", "B", "C" },
            PageSize = 2,
            CurrentPage = 0
        };

        Assert.Equal(0, paginatorState.CurrentPage);
        Assert.Equal(3, paginatorState.Items.Count);
    }

    [Fact]
    public void PaginatorState_VisibleItems_CorrectForPage()
    {
        var items = new List<string> { "A", "B", "C", "D", "E" };
        var paginatorState = new PaginatorState
        {
            Id = "p1",
            Items = items,
            PageSize = 2,
            CurrentPage = 1
        };

        var visible = paginatorState.Items
            .Skip(paginatorState.CurrentPage * paginatorState.PageSize)
            .Take(paginatorState.PageSize)
            .ToList();

        Assert.Equal(2, visible.Count);
        Assert.Equal("C", visible[0]);
        Assert.Equal("D", visible[1]);
    }

    [Fact]
    public void PaginatorState_LastPage_PartialItems()
    {
        var items = new List<string> { "A", "B", "C", "D", "E" };
        var paginatorState = new PaginatorState
        {
            Id = "p1",
            Items = items,
            PageSize = 2,
            CurrentPage = 2
        };

        var visible = paginatorState.Items
            .Skip(paginatorState.CurrentPage * paginatorState.PageSize)
            .Take(paginatorState.PageSize)
            .ToList();

        Assert.Single(visible);
        Assert.Equal("E", visible[0]);
    }
}
