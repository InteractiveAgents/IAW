using IAW.Agents.CSharp.Roslyn.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IAW.Core.Tests;

public class CallGraphBuilderTests
{
    [Fact]
    public void Build_SimpleCall_FindsEdge()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = """
            namespace Test;
            public class Foo
            {
                public void A() { B(); }
                public void B() { }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var graph = CallGraphBuilder.Build([compilation]);

        Assert.True(graph.ContainsKey("Test.Foo.A"));
        Assert.Contains("Test.Foo.B", graph["Test.Foo.A"]);
    }

    [Fact]
    public void Build_NoCalls_NoEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = """
            namespace Test;
            public class Foo
            {
                public void A() { }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var compilation = CSharpCompilation.Create("Test",
            [tree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var graph = CallGraphBuilder.Build([compilation]);

        Assert.False(graph.ContainsKey("Test.Foo.A"));
    }

    [Fact]
    public void BuildReverseGraph_ReversesEdges()
    {
        var forward = new Dictionary<string, List<string>>
        {
            ["A"] = ["B", "C"],
            ["B"] = ["C"]
        };

        var reverse = CallGraphBuilder.BuildReverseGraph(forward);

        Assert.Contains("A", reverse["B"]);
        Assert.Contains("A", reverse["C"]);
        Assert.Contains("B", reverse["C"]);
    }
}