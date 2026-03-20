using IAW.Agents.CSharp.Roslyn.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IAW.Core.Tests;

public class InheritanceTreeBuilderTests
{
    [Fact]
    public void Build_ClassImplementsInterface_Found()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = """
            namespace Test;
            public interface IFoo { }
            public class Foo : IFoo { }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var result = InheritanceTreeBuilder.Build([compilation]);

        Assert.True(result.ContainsKey("Test.Foo"));
        Assert.Contains("Test.IFoo", result["Test.Foo"].Interfaces);
    }

    [Fact]
    public void Build_DerivedTypes_ReverseIndexed()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = """
            namespace Test;
            public interface IFoo { }
            public class Foo : IFoo { }
            public class Bar : IFoo { }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var result = InheritanceTreeBuilder.Build([compilation]);

        Assert.True(result.ContainsKey("Test.IFoo"));
        Assert.Contains("Test.Foo", result["Test.IFoo"].DerivedTypes);
        Assert.Contains("Test.Bar", result["Test.IFoo"].DerivedTypes);
    }

    [Fact]
    public void Build_ClassInheritance_TracksBaseType()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = """
            namespace Test;
            public class Base { }
            public class Derived : Base { }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var result = InheritanceTreeBuilder.Build([compilation]);

        Assert.True(result.ContainsKey("Test.Derived"));
        Assert.Equal("Test.Base", result["Test.Derived"].BaseType);
        Assert.Contains("Test.Derived", result["Test.Base"].DerivedTypes);
    }

    [Fact]
    public void Build_EmptyCompilation_ReturnsEmpty()
    {
        var compilation = CSharpCompilation.Create("Empty",
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var result = InheritanceTreeBuilder.Build([compilation]);
        Assert.Empty(result);
    }
}
