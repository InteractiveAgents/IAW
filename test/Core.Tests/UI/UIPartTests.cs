using Core.UI;
using Xunit;

namespace IAW.Core.Tests.UI;

public class UIPartTests
{
    [Fact]
    public void TextPart_SerializesCorrectly()
    {
        var part = new TextPart("hello", TextStyle.Success);
        Assert.Equal("hello", part.Content);
        Assert.Equal(TextStyle.Success, part.Style);
    }

    [Fact]
    public void AgentResponse_ContainsMultipleParts()
    {
        var response = new AgentResponse([
            new TextPart("test"),
            new OptionsPart("pick one", [new Option("A", "a")], "cb-1")
        ]);
        Assert.Equal(2, response.Parts.Count);
        Assert.IsType<TextPart>(response.Parts[0]);
        Assert.IsType<OptionsPart>(response.Parts[1]);
    }

    [Fact]
    public void OptionsPart_DefaultsAllowMultipleToFalse()
    {
        var part = new OptionsPart("choose", [new Option("X", "x")], "cb-2");
        Assert.Equal("choose", part.Prompt);
        Assert.Equal("cb-2", part.CallbackId);
        Assert.False(part.AllowMultiple);
        Assert.Single(part.Options);
    }

    [Fact]
    public void CardPart_ConstructsWithFieldsAndOptionalImage()
    {
        var fields = new List<CardField> { new("Status", "Active") };
        var part = new CardPart("My Card", fields);
        Assert.Equal("My Card", part.Title);
        Assert.Single(part.Fields);
        Assert.Equal("Status", part.Fields[0].Label);
        Assert.Equal("Active", part.Fields[0].Value);
        Assert.Null(part.ImageUrl);
    }

    [Fact]
    public void CardField_ConstructsWithLabelAndValue()
    {
        var field = new CardField("Owner", "Alice");
        Assert.Equal("Owner", field.Label);
        Assert.Equal("Alice", field.Value);
    }

    [Fact]
    public void MediaPart_ConstructsWithRequiredFieldsAndNullCaption()
    {
        var part = new MediaPart("https://example.com/img.png", "img.png", "image/png");
        Assert.Equal("https://example.com/img.png", part.Url);
        Assert.Equal("img.png", part.FileName);
        Assert.Equal("image/png", part.MimeType);
        Assert.Null(part.Caption);
    }

    [Fact]
    public void ProgressPart_DefaultsPercentToNull()
    {
        var part = new ProgressPart("Working...");
        Assert.Equal("Working...", part.Message);
        Assert.Null(part.Percent);
    }

    [Fact]
    public void FormPart_ConstructsWithFieldsList()
    {
        var fields = new List<FormField>
        {
            new("name", "Your Name", FormFieldType.Text),
            new("role", "Role", FormFieldType.SingleChoice, [new Option("Dev", "dev")])
        };
        var part = new FormPart("cb-form", "Fill out the form", fields);
        Assert.Equal("cb-form", part.CallbackId);
        Assert.Equal("Fill out the form", part.Prompt);
        Assert.Equal(2, part.Fields.Count);
    }

    [Fact]
    public void FormField_DefaultsOptionsToNull()
    {
        var field = new FormField("age", "Age", FormFieldType.Number);
        Assert.Equal("age", field.Id);
        Assert.Equal("Age", field.Label);
        Assert.Equal(FormFieldType.Number, field.Type);
        Assert.Null(field.Options);
    }
}