using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class MenuTestSiloConfigurator : ISiloConfigurator
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

public class MenuTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<MenuTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    private static MenuNode SampleMenuTree() => new("Root", null, new List<MenuNode>
    {
        new("Settings", null, new List<MenuNode>
        {
            new("Language", "set-language", null),
            new("Theme", "set-theme", null)
        }),
        new("Help", "show-help", null),
        new("Projects", null, new List<MenuNode>
        {
            new("Web", null, new List<MenuNode>
            {
                new("Frontend", "open-frontend", null),
                new("Backend", "open-backend", null)
            }),
            new("Mobile", "open-mobile", null)
        })
    });

    [Fact]
    public async Task StartMenu_ReturnsInitialState()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-1");
        var root = SampleMenuTree();

        var result = await session.StartMenu("m1", root, "test-project", ct);

        Assert.Equal("m1", result.Id);
        Assert.Equal("Root", result.Root.Label);
        Assert.Empty(result.BreadCrumb);
    }

    [Fact]
    public async Task StartMenu_Idempotent_ReturnsExisting()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-2");
        var root = SampleMenuTree();

        await session.StartMenu("m2", root, "proj", ct);
        await session.NavigateMenu("m2", "Settings", ct);

        var second = await session.StartMenu("m2", root, "proj", ct);

        Assert.Single(second.BreadCrumb);
        Assert.Equal("Settings", second.BreadCrumb[0]);
    }

    [Fact]
    public async Task NavigateMenu_ChildWithChildren_PushesBreadcrumb()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-3");
        await session.StartMenu("m3", SampleMenuTree(), "proj", ct);

        var result = await session.NavigateMenu("m3", "Settings", ct);

        Assert.Single(result.BreadCrumb);
        Assert.Equal("Settings", result.BreadCrumb[0]);
    }

    [Fact]
    public async Task NavigateMenu_LeafWithAction_PushesBreadcrumb()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-4");
        await session.StartMenu("m4", SampleMenuTree(), "proj", ct);

        var result = await session.NavigateMenu("m4", "Help", ct);

        Assert.Single(result.BreadCrumb);
        Assert.Equal("Help", result.BreadCrumb[0]);
    }

    [Fact]
    public async Task NavigateMenu_DeepNavigation_BuildsBreadcrumb()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-5");
        await session.StartMenu("m5", SampleMenuTree(), "proj", ct);

        await session.NavigateMenu("m5", "Projects", ct);
        var result = await session.NavigateMenu("m5", "Web", ct);

        Assert.Equal(2, result.BreadCrumb.Count);
        Assert.Equal("Projects", result.BreadCrumb[0]);
        Assert.Equal("Web", result.BreadCrumb[1]);
    }

    [Fact]
    public async Task NavigateMenu_Back_PopsBreadcrumb()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-6");
        await session.StartMenu("m6", SampleMenuTree(), "proj", ct);
        await session.NavigateMenu("m6", "Settings", ct);

        var result = await session.NavigateMenu("m6", "__back__", ct);

        Assert.Empty(result.BreadCrumb);
    }

    [Fact]
    public async Task NavigateMenu_BackAtRoot_StaysAtRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-7");
        await session.StartMenu("m7", SampleMenuTree(), "proj", ct);

        var result = await session.NavigateMenu("m7", "__back__", ct);

        Assert.Empty(result.BreadCrumb);
    }

    [Fact]
    public async Task NavigateMenu_InvalidLabel_NoChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-8");
        await session.StartMenu("m8", SampleMenuTree(), "proj", ct);

        var result = await session.NavigateMenu("m8", "Nonexistent", ct);

        Assert.Empty(result.BreadCrumb);
    }

    [Fact]
    public async Task NavigateMenu_NonExistent_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-9");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => session.NavigateMenu("nonexistent", "Settings", ct));
    }

    [Fact]
    public async Task HandleCallback_RoutesMenuNavigation()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-10");
        await session.StartMenu("m10", SampleMenuTree(), "proj", ct);

        var result = await session.HandleCallback("cb1", "mn:m10:Settings", ct);

        Assert.NotNull(result.NewText);
        Assert.NotNull(result.Buttons);
        Assert.Contains(result.Buttons, b => b.Text == "Language");
        Assert.Contains(result.Buttons, b => b.Text == "Theme");
    }

    [Fact]
    public async Task HandleCallback_MenuLeafWithAction_ReturnsAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-11");
        await session.StartMenu("m11", SampleMenuTree(), "proj", ct);

        var result = await session.HandleCallback("cb1", "mn:m11:Help", ct);

        Assert.Equal("show-help", result.Action);
    }

    [Fact]
    public async Task HandleCallback_MenuSubFolder_ShowsBackButton()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-12");
        await session.StartMenu("m12", SampleMenuTree(), "proj", ct);

        var result = await session.HandleCallback("cb1", "mn:m12:Settings", ct);

        Assert.NotNull(result.Buttons);
        Assert.Contains(result.Buttons, b => b.CallbackData.Contains("__back__"));
    }

    [Fact]
    public async Task HandleCallback_MenuBackNavigation_Works()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-13");
        await session.StartMenu("m13", SampleMenuTree(), "proj", ct);

        await session.HandleCallback("cb1", "mn:m13:Settings", ct);
        var result = await session.HandleCallback("cb2", "mn:m13:__back__", ct);

        Assert.NotNull(result.NewText);
        Assert.NotNull(result.Buttons);
        Assert.Contains(result.Buttons, b => b.Text == "Settings");
        Assert.Contains(result.Buttons, b => b.Text == "Help");
    }

    [Fact]
    public async Task HandleCallback_MenuDeepLeaf_ReturnsAction()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("mn-14");
        await session.StartMenu("m14", SampleMenuTree(), "proj", ct);

        await session.HandleCallback("cb1", "mn:m14:Settings", ct);
        var result = await session.HandleCallback("cb2", "mn:m14:Language", ct);

        Assert.Equal("set-language", result.Action);
    }
}

public class MenuStateUnitTests
{
    [Fact]
    public void MenuState_Default_EmptyBreadcrumb()
    {
        var menuState = new MenuState
        {
            Id = "m1",
            Root = new MenuNode("Root", null, new List<MenuNode>
            {
                new("Settings", null, new List<MenuNode>
                {
                    new("Language", "set-language", null)
                })
            }),
            BreadCrumb = new List<string>()
        };

        Assert.Empty(menuState.BreadCrumb);
        Assert.Equal("Root", menuState.Root.Label);
    }

    [Fact]
    public void MenuNode_LeafNode_HasActionNoChildren()
    {
        var leaf = new MenuNode("Help", "show-help", null);

        Assert.Equal("show-help", leaf.Action);
        Assert.Null(leaf.Children);
    }

    [Fact]
    public void MenuNode_BranchNode_HasChildrenNoAction()
    {
        var branch = new MenuNode("Settings", null, new List<MenuNode>
        {
            new("Language", "set-language", null),
            new("Theme", "set-theme", null)
        });

        Assert.Null(branch.Action);
        Assert.NotNull(branch.Children);
        Assert.Equal(2, branch.Children.Count);
    }
}
