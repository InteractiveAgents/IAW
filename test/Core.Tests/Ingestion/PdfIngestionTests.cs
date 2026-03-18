using Core.Ingestion;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace IAW.Core.Tests.Ingestion;

public class PdfIngestionTests
{
    [Fact]
    public async Task ExtractChunks_SinglePage_ReturnsChunksWithCorrectMetadata()
    {
        var pdfBytes = BuildSinglePagePdf("Hello World from PdfPig test document.");
        using var stream = new MemoryStream(pdfBytes);

        var source = new PdfIngestionSource();
        var chunks = await source.ExtractChunksAsync(stream, "test.pdf", TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.Equal("test.pdf", c.FileName);
            Assert.Equal(1, c.PageNumber);
            Assert.False(string.IsNullOrWhiteSpace(c.Text));
        });
    }

    [Fact]
    public async Task ExtractChunks_MultiplePages_AssignsCorrectPageNumbers()
    {
        var pdfBytes = BuildMultiPagePdf(["Page one content.", "Page two content.", "Page three content."]);
        using var stream = new MemoryStream(pdfBytes);

        var source = new PdfIngestionSource();
        var chunks = await source.ExtractChunksAsync(stream, "multi.pdf", TestContext.Current.CancellationToken);

        Assert.NotEmpty(chunks);

        var pageNumbers = chunks.Select(c => c.PageNumber).Distinct().OrderBy(p => p).ToList();
        Assert.Contains(1, pageNumbers);
        Assert.Contains(2, pageNumbers);
        Assert.Contains(3, pageNumbers);
    }

    [Fact]
    public async Task ExtractChunks_EmptyPdf_ReturnsEmptyList()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var pdfBytes = builder.Build();
        using var stream = new MemoryStream(pdfBytes);

        var source = new PdfIngestionSource();
        var chunks = await source.ExtractChunksAsync(stream, "empty.pdf", TestContext.Current.CancellationToken);

        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var shortText = "This is a short paragraph with a few words.";
        var chunks = PdfIngestionSource.ChunkText(shortText, "test.pdf", 1);

        Assert.Single(chunks);
        Assert.Equal(shortText, chunks[0].Text);
    }

    [Fact]
    public void ChunkText_LongText_SplitsAtTargetWordCount()
    {
        var words = Enumerable.Range(1, 500).Select(i => $"word{i}");
        var longText = string.Join(' ', words);

        var chunks = PdfIngestionSource.ChunkText(longText, "test.pdf", 1);

        Assert.True(chunks.Count >= 2, $"Expected at least 2 chunks but got {chunks.Count}");
        foreach (var chunk in chunks)
        {
            var wordCount = chunk.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(wordCount <= 400, $"Chunk has {wordCount} words, exceeding 400 max.");
        }
    }

    [Fact]
    public void ChunkText_MultipleParagraphs_MergesShortParagraphs()
    {
        var text = "First short paragraph.\n\nSecond short paragraph.";

        var chunks = PdfIngestionSource.ChunkText(text, "test.pdf", 1);

        Assert.Single(chunks);
        Assert.Contains("First short paragraph.", chunks[0].Text);
        Assert.Contains("Second short paragraph.", chunks[0].Text);
    }

    [Fact]
    public void ChunkText_VeryLongParagraph_SplitsWithinParagraph()
    {
        var words = Enumerable.Range(1, 500).Select(i => $"word{i}");
        var longParagraph = string.Join(' ', words);

        var chunks = PdfIngestionSource.ChunkText(longParagraph, "test.pdf", 1);

        Assert.True(chunks.Count >= 2);
        var totalWords = chunks.Sum(c => c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(500, totalWords);
    }

    [Fact]
    public void ChunkText_PreservesPageNumberAndFileName()
    {
        var text = "Some content on page five.";

        var chunks = PdfIngestionSource.ChunkText(text, "report.pdf", 5);

        Assert.Single(chunks);
        Assert.Equal(5, chunks[0].PageNumber);
        Assert.Equal("report.pdf", chunks[0].FileName);
    }

    [Fact]
    public void IngestedChunk_HasCorrectProperties()
    {
        var chunk = new IngestedChunk("hello world", 3, "doc.pdf");

        Assert.Equal("hello world", chunk.Text);
        Assert.Equal(3, chunk.PageNumber);
        Assert.Equal("doc.pdf", chunk.FileName);
    }

    [Fact]
    public void IngestedDocument_HasCorrectProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var chunks = new List<IngestedChunk> { new("text", 1, "doc.pdf") };
        var doc = new IngestedDocument("doc.pdf", "https://blob/doc.pdf", chunks, now);

        Assert.Equal("doc.pdf", doc.FileName);
        Assert.Equal("https://blob/doc.pdf", doc.BlobUri);
        Assert.Single(doc.Chunks);
        Assert.Equal(now, doc.IngestedAt);
    }

    [Fact]
    public async Task ExtractChunks_SupportsCancellation()
    {
        var pdfBytes = BuildSinglePagePdf("Some text.");
        using var stream = new MemoryStream(pdfBytes);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var source = new PdfIngestionSource();
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.ExtractChunksAsync(stream, "test.pdf", cts.Token));
    }

    private static byte[] BuildSinglePagePdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(25, 700), font);
        return builder.Build();
    }

    private static byte[] BuildMultiPagePdf(string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(text, 12, new PdfPoint(25, 700), font);
        }

        return builder.Build();
    }
}
