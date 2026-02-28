using Core;
using Xunit;

namespace TelegramBot.Tests;

public class TelegramBotModelsTests
{
    [Fact]
    public void TelegramSendResult_Ok_SetsSuccessAndMessageId()
    {
        var result = TelegramSendResult.Ok(42);

        Assert.True(result.Success);
        Assert.Equal(42, result.MessageId);
        Assert.Null(result.Error);
    }

    [Fact]
    public void TelegramSendResult_Fail_SetsErrorAndNotSuccess()
    {
        var result = TelegramSendResult.Fail("network error");

        Assert.False(result.Success);
        Assert.Equal("network error", result.Error);
    }

    [Fact]
    public void TelegramTopicRegistry_DefaultsToEmptyTaskTopics()
    {
        var registry = new TelegramTopicRegistry();

        Assert.Equal(0, registry.AssistantThreadId);
        Assert.Equal(0, registry.NotificationsThreadId);
        Assert.Equal(0, registry.SettingsThreadId);
        Assert.NotNull(registry.TaskTopics);
        Assert.Empty(registry.TaskTopics);
    }

    [Fact]
    public void TelegramBotUpdate_DefaultValues()
    {
        var update = new TelegramBotUpdate { ChatId = 123, Text = "/start" };

        Assert.Equal(123, update.ChatId);
        Assert.Equal("/start", update.Text);
        Assert.Null(update.CallbackData);
        Assert.Null(update.ThreadId);
    }

    [Fact]
    public void TelegramInlineButton_CanSetProperties()
    {
        var button = new TelegramInlineButton { Text = "Click", CallbackData = "action:click" };

        Assert.Equal("Click", button.Text);
        Assert.Equal("action:click", button.CallbackData);
    }

    [Fact]
    public void TelegramTopicRegistry_TaskTopicsCanBePopulated()
    {
        var registry = new TelegramTopicRegistry
        {
            AssistantThreadId = 1,
            NotificationsThreadId = 2,
            SettingsThreadId = 3,
            TaskTopics = new Dictionary<string, int> { ["Fix bug"] = 10, ["Deploy"] = 11 }
        };

        Assert.Equal(2, registry.TaskTopics.Count);
        Assert.Equal(10, registry.TaskTopics["Fix bug"]);
    }
}
