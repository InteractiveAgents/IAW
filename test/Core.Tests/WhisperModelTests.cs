using Core.AI;
using Core.AI.Models;
using Xunit;

namespace IAW.Core.Tests;

public class WhisperModelTests
{
    [Fact]
    public void EnsureAllModelsLoaded_PopulatesRegistry()
    {
        WhisperModel.EnsureAllModelsLoaded();
        Assert.Contains(WhisperModel.All, m => m is WhisperLargeV3Turbo);
        Assert.Contains(WhisperModel.All, m => m is WhisperSmall);
        Assert.Contains(WhisperModel.All, m => m is WhisperTiny);
    }

    [Theory]
    [InlineData("whisper-large-v3-turbo")]
    [InlineData("whisper-small")]
    [InlineData("whisper-tiny")]
    public void FindById_ReturnsCorrectModel(string id)
    {
        WhisperModel.EnsureAllModelsLoaded();
        var model = WhisperModel.FindById(id);
        Assert.NotNull(model);
        Assert.Equal(id, model.Id);
    }

    [Fact]
    public void FindById_UnknownId_ReturnsNull()
    {
        var model = WhisperModel.FindById("whisper-nonexistent");
        Assert.Null(model);
    }

    [Fact]
    public void Priority_LargeHigherThanSmall()
    {
        WhisperModel.EnsureAllModelsLoaded();
        var large = WhisperModel.FindById("whisper-large-v3-turbo")!;
        var small = WhisperModel.FindById("whisper-small")!;
        var tiny = WhisperModel.FindById("whisper-tiny")!;
        Assert.True(large.Priority > small.Priority);
        Assert.True(small.Priority > tiny.Priority);
    }
}
