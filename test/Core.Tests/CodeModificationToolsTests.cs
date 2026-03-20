using IAW.Agents.Coding.Tools;
using Xunit;

namespace IAW.Core.Tests;

public class CodeModificationToolsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CodeModificationTools _tools;

    public CodeModificationToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"roslyn-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tools = new CodeModificationTools(() => _tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    [Fact]
    public async Task CreateFile_GeneratesValidCSharp()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Foo.cs");

        var result = await _tools.CreateFileAsync(filePath, "Test.Namespace", "Foo", "class", "");

        Assert.Contains("Foo", result);
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("class Foo", content);
        Assert.Contains("namespace Test.Namespace", content);
    }

    [Fact]
    public async Task AddUsing_AddsWhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Bar.cs");
        await File.WriteAllTextAsync(filePath, "namespace Test;\npublic class Bar { }", ct);

        var result = await _tools.AddUsingAsync(filePath, "System.Text");

        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("using System.Text;", content);
    }

    [Fact]
    public async Task AddUsing_SkipsWhenPresent()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Baz.cs");
        await File.WriteAllTextAsync(filePath, "using System.Text;\nnamespace Test;\npublic class Baz { }", ct);

        var result = await _tools.AddUsingAsync(filePath, "System.Text");

        Assert.Contains("already", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddMethod_InsertsIntoClass()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "MyClass.cs");
        await File.WriteAllTextAsync(filePath, "namespace Test;\npublic class MyClass\n{\n}", ct);

        var result = await _tools.AddMethodAsync(filePath, "MyClass", "public void DoWork()", "Console.WriteLine(\"hello\");");

        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("void DoWork", content);
    }
}
