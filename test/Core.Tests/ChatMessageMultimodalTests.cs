using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests;

public class ChatMessageMultimodalTests
{
    [Fact]
    public void Text_WithParts_ReturnsTextContent()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Parts = [new TextContent("hello"), new TextContent(" world")]
        };
        Assert.Equal("hello world", msg.Text);
    }

    [Fact]
    public void Text_WithContentOnly_FallsBack()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Content = "legacy text"
        };
        Assert.Equal("legacy text", msg.Text);
    }

    [Fact]
    public void Text_WithPartsAndContent_PrefersPartsResult()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Content = "old",
            Parts = [new TextContent("new")]
        };
        Assert.Equal("new", msg.Text);
    }

    [Fact]
    public void Text_WithEmptyParts_FallsBackToContent()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Content = "fallback",
            Parts = []
        };
        Assert.Equal("fallback", msg.Text);
    }

    [Fact]
    public void Text_WithMixedParts_ReturnsOnlyText()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Parts =
            [
                new TextContent("describe this: "),
                new ImageContent("blob://img.jpg", "image/jpeg", "photo"),
                new TextContent(" please")
            ]
        };
        Assert.Equal("describe this:  please", msg.Text);
    }

    [Fact]
    public void FileContent_StoresMetadata()
    {
        var file = new FileContent("blob://doc.pdf", "doc.pdf", "application/pdf", 245000, false);
        Assert.Equal("doc.pdf", file.FileName);
        Assert.False(file.Ingested);
    }
}
