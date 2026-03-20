using IAW.Agents.Coding.Tools;
using Xunit;

namespace IAW.Core.Tests;

public class RefactoringToolsTests : IDisposable
{
    private readonly string _tempDir;

    public RefactoringToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"refactor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, true);

    [Fact]
    public async Task RenameSymbol_RenamesClassInFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Test.cs");
        await File.WriteAllTextAsync(filePath, """
            namespace Test;
            public class OldName
            {
                public void Foo() { }
            }
            """, ct);

        var tools = new RefactoringTools(() => _tempDir, null);
        var result = await tools.RenameSymbolAsync("OldName", "NewName", filePath);
        Assert.Contains("NewName", result);
        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("class NewName", content);
        Assert.DoesNotContain("OldName", content);
    }

    [Fact]
    public async Task RenameSymbol_RenamesMethodReferences()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Refs.cs");
        await File.WriteAllTextAsync(filePath, """
            namespace Test;
            public class MyClass
            {
                public void OldMethod() { }
                public void Caller() { OldMethod(); }
            }
            """, ct);

        var tools = new RefactoringTools(() => _tempDir, null);
        var result = await tools.RenameSymbolAsync("OldMethod", "NewMethod", filePath);
        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("NewMethod", content);
        Assert.DoesNotContain("OldMethod", content);
    }
}
