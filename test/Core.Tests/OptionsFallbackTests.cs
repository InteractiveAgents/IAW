using IAW.Agents.Orchestration;
using Xunit;

namespace IAW.Core.Tests;

public class OptionsFallbackTests
{
    [Fact]
    public void DetectsNumberedListWithQuestion()
    {
        var text = """
            Here are 3 jokes:

            1. The Developer
            2. The DBA
            3. The PM

            Which one is the best?
            """;

        var result = OptionsFallbackDetector.TryDetect(text);

        Assert.NotNull(result);
        Assert.Equal(3, result.Value.Labels.Count);
        Assert.Equal("The Developer", result.Value.Labels[0]);
        Assert.Equal("The DBA", result.Value.Labels[1]);
        Assert.Equal("The PM", result.Value.Labels[2]);
    }

    [Fact]
    public void DetectsSelectTriggerWord()
    {
        var preamble = "Here is some context about colors and their meanings. " +
                       "Let me explain the differences between these options for you.\n\n";
        var text = $"{preamble}1. Red\n2. Blue\n\nPlease select one.";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.NotNull(result);
        Assert.Equal(2, result.Value.Labels.Count);
    }

    [Fact]
    public void IgnoresListInMiddleOfText()
    {
        var longPreamble = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"Line {i} of explanation."));
        var text = $"""
            {longPreamble}

            1. First item
            2. Second item

            Which do you pick?

            But actually there is more to consider. Let me explain further.
            This is a long conclusion paragraph that makes the list appear
            in the first half of the text, not the last 30%.
            More text here to push the ratio.
            Even more text.
            And more.
            """;
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresMoreThan8Items()
    {
        var items = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"{i}. Item {i}"));
        var text = $"{items}\n\nWhich one?";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresSingleItem()
    {
        var text = "1. Only one\n\nPick one?";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresLongLabels()
    {
        var longLabel = new string('A', 65);
        var text = $"1. Short\n2. {longLabel}\n\nChoose?";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresMultiLineParagraphs()
    {
        var text = """
            1. This is a long paragraph
            that spans multiple lines
            2. This is another paragraph
            with extra detail

            Which one?
            """;
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }

    [Fact]
    public void NoNumberedList_ReturnsNull()
    {
        var text = "Just a plain response with no options.";
        var result = OptionsFallbackDetector.TryDetect(text);
        Assert.Null(result);
    }
}
