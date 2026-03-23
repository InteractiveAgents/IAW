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

    [Fact]
    public async Task ExtractMethod_ExtractsStatements()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Extract.cs");
        await File.WriteAllTextAsync(filePath, """
            namespace Test;
            public class Calc
            {
                public int Compute()
                {
                    var a = 1;
                    var b = 2;
                    var sum = a + b;
                    return sum;
                }
            }
            """, ct);

        var tools = new RefactoringTools(() => _tempDir, null);
        var result = await tools.ExtractMethodAsync(filePath, 8, 8, "AddNumbers");
        Assert.Contains("AddNumbers", result);
        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("AddNumbers", content);
    }

    [Fact]
    public async Task ChangeSignature_ReplacesParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Sig.cs");
        await File.WriteAllTextAsync(filePath, """
            namespace Test;
            public class Svc
            {
                public string Greet() { return "hi"; }
            }
            """, ct);

        var tools = new RefactoringTools(() => _tempDir, null);
        var result = await tools.ChangeSignatureAsync(filePath, "Svc", "Greet", "string name");
        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("string name", content);
    }

    [Fact]
    public async Task MoveType_MovesToNewFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sourceFile = Path.Combine(_tempDir, "Source.cs");
        await File.WriteAllTextAsync(sourceFile, """
            namespace Test;
            public class Stay { }
            public class ToMove { }
            """, ct);
        var targetFile = Path.Combine(_tempDir, "Target.cs");

        var tools = new RefactoringTools(() => _tempDir, null);
        var result = await tools.MoveTypeAsync(sourceFile, "ToMove", targetFile);
        Assert.Contains("Moved", result);
        Assert.True(File.Exists(targetFile));
        var sourceContent = await File.ReadAllTextAsync(sourceFile, ct);
        Assert.DoesNotContain("ToMove", sourceContent);
        Assert.Contains("Stay", sourceContent);
        var targetContent = await File.ReadAllTextAsync(targetFile, ct);
        Assert.Contains("class ToMove", targetContent);
    }

    [Fact]
    public async Task InlineVariable_ReplacesUsages()
    {
        var ct = TestContext.Current.CancellationToken;
        var filePath = Path.Combine(_tempDir, "Inline.cs");
        await File.WriteAllTextAsync(filePath, """
            namespace Test;
            public class Calc
            {
                public int Get()
                {
                    var x = 42;
                    return x;
                }
            }
            """, ct);

        var tools = new RefactoringTools(() => _tempDir, null);
        var result = await tools.InlineVariableAsync(filePath, "x", 6);
        Assert.Contains("Inlined", result);
        var content = await File.ReadAllTextAsync(filePath, ct);
        Assert.Contains("return 42", content);
    }
}