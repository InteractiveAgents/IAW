using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class WizardTestSiloConfigurator : ISiloConfigurator
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

public class WizardTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<WizardTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    private static List<Button> NoOptions() => new();
    private static List<Button> Buttons(params Button[] buttons) => new(buttons);

    [Fact]
    public async Task StartWizard_ReturnsInitialState()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-1");
        var steps = new WizardStep[]
        {
            new("name", "What's your name?", NoOptions()),
            new("color", "Pick a color",
                Buttons(new Button("Red", "wz:w1:Red", null), new Button("Blue", "wz:w1:Blue", null)))
        };

        var result = await session.StartWizard("w1", steps, "test-project", ct);

        Assert.Equal("w1", result.Id);
        Assert.Equal(0, result.CurrentStep);
        Assert.Equal(2, result.Steps.Count);
        Assert.Empty(result.Collected);
    }

    [Fact]
    public async Task AdvanceWizard_CollectsSelectionAndMoves()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-2");
        var steps = new WizardStep[]
        {
            new("name", "What's your name?", NoOptions()),
            new("color", "Pick a color",
                Buttons(new Button("Red", "wz:w2:Red", null)))
        };
        await session.StartWizard("w2", steps, "proj", ct);

        var result = await session.AdvanceWizard("w2", "Alice", ct);

        Assert.Equal(1, result.CurrentStep);
        Assert.Equal("Alice", result.Collected["name"]);
    }

    [Fact]
    public async Task AdvanceWizard_CompletesWhenAllStepsDone()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-3");
        var steps = new WizardStep[]
        {
            new("name", "What's your name?", NoOptions()),
            new("color", "Pick a color",
                Buttons(new Button("Red", "wz:w3:Red", null)))
        };
        await session.StartWizard("w3", steps, "proj", ct);
        await session.AdvanceWizard("w3", "Alice", ct);
        var result = await session.AdvanceWizard("w3", "Red", ct);

        Assert.True(result.CurrentStep >= result.Steps.Count);
        Assert.Equal("Alice", result.Collected["name"]);
        Assert.Equal("Red", result.Collected["color"]);
    }

    [Fact]
    public async Task HandleCallback_RoutesWizardCallback()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-4");
        var steps = new WizardStep[]
        {
            new("color", "Pick a color",
                Buttons(new Button("Red", "wz:w4:Red", null), new Button("Blue", "wz:w4:Blue", null))),
            new("size", "Pick a size",
                Buttons(new Button("S", "wz:w4:S", null), new Button("L", "wz:w4:L", null)))
        };
        await session.StartWizard("w4", steps, "proj", ct);

        var result = await session.HandleCallback("cb1", "wz:w4:Red", ct);

        Assert.Equal("Pick a size", result.NewText);
        Assert.Null(result.Toast);
    }

    [Fact]
    public async Task HandleCallback_WizardCompleted_ReturnToast()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-5");
        var steps = new WizardStep[]
        {
            new("color", "Pick a color",
                Buttons(new Button("Red", "wz:w5:Red", null)))
        };
        await session.StartWizard("w5", steps, "proj", ct);

        var result = await session.HandleCallback("cb1", "wz:w5:Red", ct);

        Assert.Equal("Wizard completed", result.Toast);
    }

    [Fact]
    public async Task HasPendingFreeTextInput_TrueForFreeTextStep()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-6");
        var steps = new WizardStep[]
        {
            new("color", "Pick a color",
                Buttons(new Button("Red", "wz:w6:Red", null))),
            new("name", "What's your name?", NoOptions())
        };
        await session.StartWizard("w6", steps, "my-topic", ct);

        // advance past button step to free text step
        await session.AdvanceWizard("w6", "Red", ct);

        var pending = await session.HasPendingFreeTextInput("my-topic", ct);
        Assert.True(pending);
    }

    [Fact]
    public async Task HasPendingFreeTextInput_FalseByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-7");
        var pending = await session.HasPendingFreeTextInput("nonexistent", ct);
        Assert.False(pending);
    }

    [Fact]
    public async Task AdvanceWizard_NonExistent_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("wiz-8");
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => session.AdvanceWizard("nonexistent", "value", ct));
    }
}

public class WizardStateUnitTests
{
    [Fact]
    public void WizardState_InitialStep_IsZero()
    {
        var steps = new List<WizardStep>
        {
            new("name", "What's your name?", new List<Button>()),
            new("color", "Pick a color",
                new List<Button> { new("Red", "wz:w1:Red", null), new("Blue", "wz:w1:Blue", null) })
        };
        var wizardState = new WizardState
        {
            Id = "w1",
            Steps = steps,
            CurrentStep = 0,
            Collected = new Dictionary<string, string>()
        };

        Assert.Equal(0, wizardState.CurrentStep);
        Assert.Equal(2, wizardState.Steps.Count);
        Assert.Empty(wizardState.Collected);
    }

    [Fact]
    public void WizardState_IsCompleted_WhenCurrentStepExceedsCount()
    {
        var wizardState = new WizardState
        {
            Id = "w1",
            Steps = new List<WizardStep> { new("name", "What's your name?", new List<Button>()) },
            CurrentStep = 1,
            Collected = new Dictionary<string, string> { ["name"] = "Bob" }
        };

        Assert.True(wizardState.CurrentStep >= wizardState.Steps.Count);
    }

    [Fact]
    public void WizardStep_FreeText_HasEmptyOptions()
    {
        var step = new WizardStep("name", "What's your name?", new List<Button>());
        Assert.Empty(step.Options);
    }

    [Fact]
    public void WizardStep_ButtonChoice_HasOptions()
    {
        var step = new WizardStep("color", "Pick a color",
            new List<Button> { new("Red", "wz:w1:Red", null), new("Blue", "wz:w1:Blue", null) });
        Assert.Equal(2, step.Options.Count);
    }
}
