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
}
