using IAW.Agents.Coding;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class OrchestrationCompilerTests
{
    [Fact]
    public void Compile_valid_source_succeeds()
    {
        var source = """
            using System;
            Console.WriteLine("hello");
            """;
        var result = OrchestrationCompiler.Compile(source);
        Assert.True(result.Success);
    }

    [Fact]
    public void Compile_invalid_source_returns_errors()
    {
        var source = "int x = \"not a number\";";
        var result = OrchestrationCompiler.Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Compile_empty_source_succeeds()
    {
        var result = OrchestrationCompiler.Compile("");
        Assert.True(result.Success);
    }
}
