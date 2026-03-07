using IAW.Core.Tools;
using Xunit;

namespace IAW.Core.Tests;

public class FileToolsTests : IDisposable
{
    private readonly string _workspace;
    private readonly FileTools _tools;

    public FileToolsTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "iaw-filetools-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workspace);
        _tools = new FileTools(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, true);
    }

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        var filePath = Path.Combine(_workspace, "test.txt");
        await File.WriteAllTextAsync(filePath, "hello world", TestContext.Current.CancellationToken);

        var result = await _tools.ReadFileAsync("test.txt");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ReadFile_MissingFile_ReturnsNotFound()
    {
        var result = await _tools.ReadFileAsync("missing.txt");

        Assert.Contains("File not found", result);
    }

    [Fact]
    public async Task WriteFile_CreatesFileAndSubdirectory()
    {
        var result = await _tools.WriteFileAsync("sub/dir/file.txt", "content");

        Assert.Contains("File written", result);
        Assert.True(File.Exists(Path.Combine(_workspace, "sub", "dir", "file.txt")));
        Assert.Equal("content", await File.ReadAllTextAsync(Path.Combine(_workspace, "sub", "dir", "file.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteFile_OutsideWorkspace_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _tools.WriteFileAsync(Path.Combine(_workspace, "..", "escape.txt"), "bad"));
    }

    [Fact]
    public void ListFiles_MatchesPattern()
    {
        File.WriteAllText(Path.Combine(_workspace, "a.cs"), "");
        File.WriteAllText(Path.Combine(_workspace, "b.txt"), "");

        var results = _tools.ListFiles(".", "*.cs");

        Assert.Single(results);
        Assert.Contains("a.cs", results[0]);
    }

    [Fact]
    public void ListFiles_ExcludesGitDirectory()
    {
        var gitDir = Path.Combine(_workspace, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), "");
        File.WriteAllText(Path.Combine(_workspace, "real.cs"), "");

        var results = _tools.ListFiles(".", "*");

        Assert.DoesNotContain(results, r => r.Contains(".git"));
        Assert.Contains(results, r => r.Contains("real.cs"));
    }

    [Fact]
    public void SearchCode_FindsMatch()
    {
        File.WriteAllText(Path.Combine(_workspace, "search.cs"), "public class MyAgent { }");

        var results = _tools.SearchCode("MyAgent", ".");

        Assert.Single(results);
        Assert.Contains("MyAgent", results[0]);
    }

    [Fact]
    public void SearchCode_NoMatch_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_workspace, "search.cs"), "public class Foo { }");

        var results = _tools.SearchCode("NonExistent", ".");

        Assert.Empty(results);
    }

    [Fact]
    public void ListFiles_MissingDirectory_ReturnsNotFound()
    {
        var results = _tools.ListFiles("nonexistent", "*");

        Assert.Single(results);
        Assert.Contains("Directory not found", results[0]);
    }
}
