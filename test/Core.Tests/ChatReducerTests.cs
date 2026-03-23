using Core.Agents;
using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests;

public class ChatReducerTests
{
    private readonly ChatReducer _reducer = new();

    [Fact]
    public void Reduce_LastMessageAlwaysPreserved()
    {
        var history = CreateMessages(25);
        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);
        Assert.Equal(history[^1], result[^1]);
    }

    [Fact]
    public void Reduce_RecentWindowVerbatim()
    {
        var history = CreateMessages(30);
        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);
        Assert.Equal(20, result.Count);
        for (var i = 0; i < 20; i++)
            Assert.Equal(history[10 + i], result[i]);
    }

    [Fact]
    public void Reduce_SummaryPrepended()
    {
        var history = CreateMessages(25);
        var summary = new ChatMessage { Role = "system", Content = "Summary of earlier conversation", Parts = [new TextContent("Summary of earlier conversation")] };
        var result = _reducer.Reduce(history, summary, recentWindow: 20);
        Assert.Equal("system", result[0].Role);
        Assert.Contains("Summary", result[0].Text);
    }

    [Fact]
    public void Reduce_NonReducibleMessagesPinned()
    {
        var history = new List<ChatMessage>();
        for (var i = 0; i < 10; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Message {i}", Parts = [new TextContent($"Message {i}")] });

        history.Add(new ChatMessage { Role = "user", Parts = [new FileContent("blob://x", "doc.pdf", "application/pdf", 1024, false)] });

        for (var i = 0; i < 20; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Recent {i}", Parts = [new TextContent($"Recent {i}")] });

        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);
        // pinned file message + 20 recent = 21
        Assert.Equal(21, result.Count);
        Assert.Contains(result, m => m.Parts.Any(p => p is FileContent));
    }

    [Fact]
    public void Reduce_ShortHistory_NoReduction()
    {
        var history = CreateMessages(10);
        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void Reduce_ExactWindowSize_NoReduction()
    {
        var history = CreateMessages(20);
        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);
        Assert.Equal(20, result.Count);
    }

    [Fact]
    public void Reduce_EmptyHistory_ReturnsEmpty()
    {
        var result = _reducer.Reduce([], summary: null, recentWindow: 20);
        Assert.Empty(result);
    }

    [Fact]
    public void Reduce_EmptyHistoryWithSummary_ReturnsSummaryOnly()
    {
        var summary = new ChatMessage { Role = "system", Content = "Summary", Parts = [new TextContent("Summary")] };
        var result = _reducer.Reduce([], summary, recentWindow: 20);
        Assert.Single(result);
        Assert.Equal("system", result[0].Role);
    }

    [Fact]
    public void Reduce_SummaryAndNonReducibleAndRecent_CorrectOrder()
    {
        var history = new List<ChatMessage>();
        // 5 old messages, one with "remember"
        for (var i = 0; i < 4; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Old {i}", Parts = [new TextContent($"Old {i}")] });
        history.Add(new ChatMessage { Role = "user", Content = "Please remember my preference", Parts = [new TextContent("Please remember my preference")] });

        // 5 recent messages
        for (var i = 0; i < 5; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Recent {i}", Parts = [new TextContent($"Recent {i}")] });

        var summary = new ChatMessage { Role = "system", Content = "Earlier summary", Parts = [new TextContent("Earlier summary")] };
        var result = _reducer.Reduce(history, summary, recentWindow: 5);

        // order: summary, pinned "remember" message, 5 recent
        Assert.Equal(7, result.Count);
        Assert.Equal("Earlier summary", result[0].Text);
        Assert.Contains("remember", result[1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Recent 0", result[2].Text);
    }

    [Fact]
    public void IsNonReducible_FileContent_ReturnsTrue()
    {
        var msg = new ChatMessage { Role = "user", Parts = [new FileContent("blob://x", "doc.pdf", "application/pdf", 1024, false)] };
        Assert.True(ChatReducer.IsNonReducible(msg));
    }

    [Fact]
    public void IsNonReducible_ImageContent_ReturnsTrue()
    {
        var msg = new ChatMessage { Role = "user", Parts = [new ImageContent("blob://x", "image/jpeg", "A photo")] };
        Assert.True(ChatReducer.IsNonReducible(msg));
    }

    [Fact]
    public void IsNonReducible_RememberKeyword_ReturnsTrue()
    {
        var msg = new ChatMessage { Role = "user", Content = "Please remember that I prefer dark mode", Parts = [new TextContent("Please remember that I prefer dark mode")] };
        Assert.True(ChatReducer.IsNonReducible(msg));
    }

    [Fact]
    public void IsNonReducible_ApprovalKeyword_ReturnsTrue()
    {
        var msg = new ChatMessage { Role = "user", Content = "Waiting for approval", Parts = [new TextContent("Waiting for approval")] };
        Assert.True(ChatReducer.IsNonReducible(msg));
    }

    [Fact]
    public void IsNonReducible_NormalMessage_ReturnsFalse()
    {
        var msg = new ChatMessage { Role = "user", Content = "What's the weather?", Parts = [new TextContent("What's the weather?")] };
        Assert.False(ChatReducer.IsNonReducible(msg));
    }

    [Fact]
    public void Reduce_EvictsImagesFromOldMessages()
    {
        var history = new List<ChatMessage>();
        history.Add(new ChatMessage { Role = "user", Parts = [new ImageContent("blob://x", "image/jpeg", "A sunset photo")] });
        for (var i = 0; i < 20; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Recent {i}", Parts = [new TextContent($"Recent {i}")] });

        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);

        var pinnedMsg = result[0];
        Assert.DoesNotContain(pinnedMsg.Parts, p => p is ImageContent);
        Assert.Contains(pinnedMsg.Parts, p => p is TextContent tc && tc.Text.Contains("A sunset photo"));
    }

    [Fact]
    public void Reduce_PreservesImagesInRecentWindow()
    {
        var history = new List<ChatMessage>();
        for (var i = 0; i < 5; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Msg {i}", Parts = [new TextContent($"Msg {i}")] });
        history.Add(new ChatMessage { Role = "user", Parts = [new ImageContent("blob://y", "image/png", "A chart")] });

        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);

        Assert.Contains(result, m => m.Parts.Any(p => p is ImageContent));
    }

    [Fact]
    public void Reduce_EvictImages_FallsBackToMimeType_WhenNoCaption()
    {
        var history = new List<ChatMessage>();
        history.Add(new ChatMessage { Role = "user", Parts = [new ImageContent("blob://x", "image/jpeg", null)] });
        for (var i = 0; i < 20; i++)
            history.Add(new ChatMessage { Role = "user", Content = $"Recent {i}", Parts = [new TextContent($"Recent {i}")] });

        var result = _reducer.Reduce(history, summary: null, recentWindow: 20);
        var pinnedMsg = result[0];
        Assert.Contains(pinnedMsg.Parts, p => p is TextContent tc && tc.Text.Contains("image/jpeg"));
    }

    private static List<ChatMessage> CreateMessages(int count)
    {
        return [.. Enumerable.Range(0, count)
            .Select(i => new ChatMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"Message {i}",
                Parts = [new TextContent($"Message {i}")]
            })];
    }
}