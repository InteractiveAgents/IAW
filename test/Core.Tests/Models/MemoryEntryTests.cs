using Core.Models;
using Xunit;

namespace IAW.Core.Tests.Models;

public class MemoryEntryTests
{
    [Fact]
    public void MemoryProvenance_trust_scores_are_valid()
    {
        var provenance = new MemoryProvenance("user-input", null, null, null, DateTimeOffset.UtcNow, null, 1.0f);
        Assert.Equal(1.0f, provenance.TrustScore);
    }

    [Fact]
    public void MemoryEntry_tracks_supersession()
    {
        var entry = new MemoryEntry("id-1", "user likes tabs",
            new MemoryProvenance("user-input", null, null, null, DateTimeOffset.UtcNow, null, 1.0f),
            1.0f, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, "id-2");
        Assert.Equal("id-2", entry.SupersededBy);
    }

    [Fact]
    public void MemoryEntry_default_not_superseded()
    {
        var entry = new MemoryEntry("id-1", "some fact",
            new MemoryProvenance("conversation", null, null, null, DateTimeOffset.UtcNow, null, 0.9f),
            0.9f, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null);
        Assert.Null(entry.SupersededBy);
    }

    [Fact]
    public void MemoryProvenance_all_sources_have_expected_trust()
    {
        Assert.Equal(1.0f, new MemoryProvenance("user-input", null, null, null, DateTimeOffset.UtcNow, null, 1.0f).TrustScore);
        Assert.Equal(0.9f, new MemoryProvenance("conversation", null, null, null, DateTimeOffset.UtcNow, null, 0.9f).TrustScore);
        Assert.Equal(0.7f, new MemoryProvenance("task-stream", null, null, null, DateTimeOffset.UtcNow, null, 0.7f).TrustScore);
        Assert.Equal(0.6f, new MemoryProvenance("pattern-inference", null, null, null, DateTimeOffset.UtcNow, null, 0.6f).TrustScore);
    }
}