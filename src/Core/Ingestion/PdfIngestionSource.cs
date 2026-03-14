using UglyToad.PdfPig;

namespace Core.Ingestion;

public sealed class PdfIngestionSource : IIngestionSource
{
    private const int TargetWordsPerChunk = 200;
    private const int MaxWordsPerChunk = 400;

    public Task<IReadOnlyList<IngestedChunk>> ExtractChunksAsync(Stream source, string fileName, CancellationToken ct = default)
    {
        using var memoryStream = new MemoryStream();
        source.CopyTo(memoryStream);
        var pdfBytes = memoryStream.ToArray();

        var chunks = new List<IngestedChunk>();

        using var document = PdfDocument.Open(pdfBytes);
        for (var pageIndex = 1; pageIndex <= document.NumberOfPages; pageIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var page = document.GetPage(pageIndex);
            var pageText = page.Text?.Trim();
            if (string.IsNullOrEmpty(pageText))
                continue;

            var pageChunks = ChunkText(pageText, fileName, pageIndex);
            chunks.AddRange(pageChunks);
        }

        return Task.FromResult<IReadOnlyList<IngestedChunk>>(chunks);
    }

    public static List<IngestedChunk> ChunkText(string text, string fileName, int pageNumber)
    {
        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<IngestedChunk>();
        var currentWords = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            var trimmed = paragraph.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var paragraphWords = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            // If adding this paragraph would exceed max, flush current buffer first
            if (currentWords.Count > 0 && currentWords.Count + paragraphWords.Length > MaxWordsPerChunk)
            {
                chunks.Add(new IngestedChunk(string.Join(' ', currentWords), pageNumber, fileName));
                currentWords.Clear();
            }

            // If a single paragraph exceeds max, split it into smaller chunks
            if (paragraphWords.Length > MaxWordsPerChunk)
            {
                foreach (var wordBatch in SplitIntoChunks(paragraphWords, TargetWordsPerChunk))
                {
                    chunks.Add(new IngestedChunk(string.Join(' ', wordBatch), pageNumber, fileName));
                }
                continue;
            }

            currentWords.AddRange(paragraphWords);

            // Flush when we reach the target size
            if (currentWords.Count >= TargetWordsPerChunk)
            {
                chunks.Add(new IngestedChunk(string.Join(' ', currentWords), pageNumber, fileName));
                currentWords.Clear();
            }
        }

        // Flush remaining words
        if (currentWords.Count > 0)
        {
            chunks.Add(new IngestedChunk(string.Join(' ', currentWords), pageNumber, fileName));
        }

        return chunks;
    }

    private static IEnumerable<string[]> SplitIntoChunks(string[] words, int chunkSize)
    {
        for (var i = 0; i < words.Length; i += chunkSize)
        {
            var length = Math.Min(chunkSize, words.Length - i);
            var chunk = new string[length];
            Array.Copy(words, i, chunk, 0, length);
            yield return chunk;
        }
    }
}
