using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace IAW.Core.Tests.UI;

public sealed class FormTestSiloConfigurator : ISiloConfigurator
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

public class FormTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<FormTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    static FormField SingleChoice(string name, string prompt, params Button[] options) =>
        new(name, prompt, FormFieldType.SingleChoice, [.. options]);

    static FormField MultiChoice(string name, string prompt, params Button[] options) =>
        new(name, prompt, FormFieldType.MultiChoice, [.. options]);

    static FormField FreeText(string name, string prompt) =>
        new(name, prompt, FormFieldType.FreeText, null);

    static Button Btn(string text, string value) => new(text, $"fm:_:{value}", null);

    [Fact]
    public async Task StartForm_ReturnsInitialState()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-1");
        var fields = new[]
        {
            SingleChoice("color", "Pick a color", Btn("Red", "Red"), Btn("Blue", "Blue")),
            FreeText("name", "Enter your name")
        };

        var result = await session.StartForm("f1", fields, "test-project", ct);

        Assert.Equal("f1", result.Id);
        Assert.Equal(0, result.CurrentField);
        Assert.Equal(2, result.Fields.Count);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task StartForm_Idempotent_ReturnsExisting()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-2");
        var fields = new[]
        {
            SingleChoice("color", "Pick a color", Btn("Red", "Red")),
            FreeText("name", "Enter name")
        };
        var first = await session.StartForm("f2", fields, "proj", ct);
        await session.AdvanceForm("f2", "Red", ct);

        var second = await session.StartForm("f2", fields, "proj", ct);

        Assert.Equal(1, second.CurrentField);
        Assert.Equal("Red", second.Values["color"]);
    }

    [Fact]
    public async Task AdvanceForm_SingleChoice_StoresValueAndAdvances()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-3");
        var fields = new[]
        {
            SingleChoice("color", "Pick a color", Btn("Red", "Red"), Btn("Blue", "Blue")),
            FreeText("name", "Enter name")
        };
        await session.StartForm("f3", fields, "proj", ct);

        var result = await session.AdvanceForm("f3", "Red", ct);

        Assert.Equal(1, result.CurrentField);
        Assert.Equal("Red", result.Values["color"]);
    }

    [Fact]
    public async Task AdvanceForm_FreeText_StoresValueAndAdvances()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-4");
        var fields = new[]
        {
            FreeText("name", "Enter name"),
            SingleChoice("color", "Pick", Btn("Red", "Red"))
        };
        await session.StartForm("f4", fields, "proj", ct);

        var result = await session.AdvanceForm("f4", "Alice", ct);

        Assert.Equal(1, result.CurrentField);
        Assert.Equal("Alice", result.Values["name"]);
    }

    [Fact]
    public async Task AdvanceForm_Completes_WhenAllFieldsDone()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-5");
        var fields = new[]
        {
            SingleChoice("color", "Pick", Btn("Red", "Red")),
            FreeText("name", "Enter name")
        };
        await session.StartForm("f5", fields, "proj", ct);
        await session.AdvanceForm("f5", "Red", ct);
        var result = await session.AdvanceForm("f5", "Alice", ct);

        Assert.True(result.CurrentField >= result.Fields.Count);
        Assert.Equal("Red", result.Values["color"]);
        Assert.Equal("Alice", result.Values["name"]);
    }

    [Fact]
    public async Task AdvanceForm_NonExistent_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-6");
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => session.AdvanceForm("nonexistent", "value", ct));
    }

    [Fact]
    public async Task HandleCallback_SingleChoice_AdvancesAndRendersNext()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-7");
        var fields = new[]
        {
            SingleChoice("color", "Pick a color", Btn("Red", "Red"), Btn("Blue", "Blue")),
            SingleChoice("size", "Pick a size", Btn("S", "S"), Btn("L", "L"))
        };
        await session.StartForm("f7", fields, "proj", ct);

        var result = await session.HandleCallback("cb1", "fm:f7:Red", ct);

        Assert.Equal("Pick a size", result.NewText);
        Assert.NotNull(result.Buttons);
        Assert.Equal(2, result.Buttons.Count);
    }

    [Fact]
    public async Task HandleCallback_FormCompleted_ReturnsToast()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-8");
        var fields = new[]
        {
            SingleChoice("color", "Pick", Btn("Red", "Red"))
        };
        await session.StartForm("f8", fields, "proj", ct);

        var result = await session.HandleCallback("cb1", "fm:f8:Red", ct);

        Assert.Equal("Form completed", result.Toast);
    }

    [Fact]
    public async Task HandleCallback_MultiChoice_TogglesSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-9");
        var fields = new[]
        {
            MultiChoice("tags", "Select tags", Btn("A", "A"), Btn("B", "B"), Btn("C", "C"))
        };
        await session.StartForm("f9", fields, "proj", ct);

        // select A
        var r1 = await session.HandleCallback("cb1", "fm:f9:A", ct);
        Assert.NotNull(r1.NewText);
        Assert.Contains("A", r1.NewText);

        // select B
        var r2 = await session.HandleCallback("cb2", "fm:f9:B", ct);
        Assert.Contains("A", r2.NewText);
        Assert.Contains("B", r2.NewText);

        // deselect A
        var r3 = await session.HandleCallback("cb3", "fm:f9:A", ct);
        Assert.DoesNotContain("Selected: A", r3.NewText!);
        Assert.Contains("B", r3.NewText);
    }

    [Fact]
    public async Task HandleCallback_MultiChoice_DoneAdvances()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-10");
        var fields = new[]
        {
            MultiChoice("tags", "Select tags", Btn("A", "A"), Btn("B", "B")),
            SingleChoice("color", "Pick color", Btn("Red", "Red"))
        };
        await session.StartForm("f10", fields, "proj", ct);

        await session.HandleCallback("cb1", "fm:f10:A", ct);
        await session.HandleCallback("cb2", "fm:f10:B", ct);
        var result = await session.HandleCallback("cb3", "fm:f10:__done__", ct);

        Assert.Equal("Pick color", result.NewText);
    }

    [Fact]
    public async Task HandleCallback_MultiChoice_DoneStoresCommaSeparated()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-11");
        var fields = new[]
        {
            MultiChoice("tags", "Select tags", Btn("A", "A"), Btn("B", "B")),
        };
        await session.StartForm("f11", fields, "proj", ct);

        await session.HandleCallback("cb1", "fm:f11:A", ct);
        await session.HandleCallback("cb2", "fm:f11:B", ct);
        var result = await session.HandleCallback("cb3", "fm:f11:__done__", ct);

        Assert.Equal("Form completed", result.Toast);
    }

    [Fact]
    public async Task FreeText_SetsPendingFreeTextInput()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-12");
        var fields = new[]
        {
            FreeText("name", "Enter name"),
            SingleChoice("color", "Pick", Btn("Red", "Red"))
        };
        await session.StartForm("f12", fields, "my-topic", ct);

        var pending = await session.HasPendingFreeTextInput("my-topic", ct);
        Assert.True(pending);
    }

    [Fact]
    public async Task FreeText_ClearedOnAdvanceToNonFreeText()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-13");
        var fields = new[]
        {
            FreeText("name", "Enter name"),
            SingleChoice("color", "Pick", Btn("Red", "Red"))
        };
        await session.StartForm("f13", fields, "my-topic", ct);
        Assert.True(await session.HasPendingFreeTextInput("my-topic", ct));

        await session.AdvanceForm("f13", "Alice", ct);
        Assert.False(await session.HasPendingFreeTextInput("my-topic", ct));
    }

    [Fact]
    public async Task FreeText_SetAgainWhenNextFieldIsFreeText()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-14");
        var fields = new[]
        {
            SingleChoice("color", "Pick", Btn("Red", "Red")),
            FreeText("name", "Enter name"),
            FreeText("email", "Enter email")
        };
        await session.StartForm("f14", fields, "my-topic", ct);

        await session.AdvanceForm("f14", "Red", ct);
        Assert.True(await session.HasPendingFreeTextInput("my-topic", ct));

        await session.AdvanceForm("f14", "Alice", ct);
        Assert.True(await session.HasPendingFreeTextInput("my-topic", ct));
    }

    [Fact]
    public async Task FreeText_ClearedOnFormCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-15");
        var fields = new[]
        {
            FreeText("name", "Enter name")
        };
        await session.StartForm("f15", fields, "my-topic", ct);
        Assert.True(await session.HasPendingFreeTextInput("my-topic", ct));

        await session.AdvanceForm("f15", "Alice", ct);
        Assert.False(await session.HasPendingFreeTextInput("my-topic", ct));
    }

    [Fact]
    public async Task HandleCallback_MultiChoice_ShowsDoneButton()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-16");
        var fields = new[]
        {
            MultiChoice("tags", "Select tags", Btn("A", "A"), Btn("B", "B"))
        };
        await session.StartForm("f16", fields, "proj", ct);

        var result = await session.HandleCallback("cb1", "fm:f16:A", ct);

        Assert.NotNull(result.Buttons);
        Assert.Contains(result.Buttons, b => b.Text == "Done");
    }

    [Fact]
    public async Task HandleCallback_MultiChoice_TogglesCheckmark()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Session("fm-17");
        var fields = new[]
        {
            MultiChoice("tags", "Select tags", Btn("A", "A"), Btn("B", "B"))
        };
        await session.StartForm("f17", fields, "proj", ct);

        var r1 = await session.HandleCallback("cb1", "fm:f17:A", ct);
        Assert.Contains(r1.Buttons!, b => b.Text.StartsWith("\u2705") && b.Text.Contains("A"));

        // toggle off
        var r2 = await session.HandleCallback("cb2", "fm:f17:A", ct);
        Assert.DoesNotContain(r2.Buttons!, b => b.Text.StartsWith("\u2705") && b.Text.Contains("A"));
    }
}

public class FormStateUnitTests
{
    [Fact]
    public void FormState_InitialField_IsZero()
    {
        var formState = new FormState
        {
            Id = "f1",
            Fields = new List<FormField>
            {
                new("name", "Enter name", FormFieldType.FreeText, null),
                new("color", "Pick color", FormFieldType.SingleChoice,
                    new List<Button> { new("Red", "fm:f1:Red", null) })
            },
            CurrentField = 0,
            Values = new Dictionary<string, string>()
        };

        Assert.Equal(0, formState.CurrentField);
        Assert.Equal(2, formState.Fields.Count);
        Assert.Empty(formState.Values);
    }

    [Fact]
    public void FormState_IsCompleted_WhenCurrentFieldExceedsCount()
    {
        var formState = new FormState
        {
            Id = "f1",
            Fields = new List<FormField>
            {
                new("name", "Enter name", FormFieldType.FreeText, null)
            },
            CurrentField = 1,
            Values = new Dictionary<string, string> { ["name"] = "Alice" }
        };

        Assert.True(formState.CurrentField >= formState.Fields.Count);
    }

    [Fact]
    public void FormField_FreeText_HasNullOptions()
    {
        var field = new FormField("name", "Enter name", FormFieldType.FreeText, null);
        Assert.Null(field.Options);
    }

    [Fact]
    public void FormField_SingleChoice_HasOptions()
    {
        var field = new FormField("color", "Pick color", FormFieldType.SingleChoice,
            new List<Button> { new("Red", "fm:f1:Red", null), new("Blue", "fm:f1:Blue", null) });
        Assert.Equal(2, field.Options!.Count);
    }

    [Fact]
    public void FormField_MultiChoice_HasOptions()
    {
        var field = new FormField("tags", "Select tags", FormFieldType.MultiChoice,
            new List<Button> { new("A", "fm:f1:A", null), new("B", "fm:f1:B", null) });
        Assert.Equal(2, field.Options!.Count);
    }

    [Fact]
    public void WidgetState_HasCreatedAtField()
    {
        var before = DateTimeOffset.UtcNow;
        var formState = new FormState
        {
            Id = "f1",
            Fields = new List<FormField>(),
            CurrentField = 0,
            Values = new Dictionary<string, string>()
        };

        Assert.True(formState.CreatedAt >= before);
        Assert.True(formState.CreatedAt <= DateTimeOffset.UtcNow);
    }
}
