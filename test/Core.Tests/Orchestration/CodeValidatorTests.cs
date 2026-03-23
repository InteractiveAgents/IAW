using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class CodeValidatorTests
{
    [Fact]
    public void Sanitize_RemovesInvalidUsings()
    {
        var code = """
            using System.Text.Json;
            using IAW.Agents.LLM;
            using IAW.Agents.System;

            await using var iaw = await IAWCluster.Connect(args);
            """;

        var result = CodeValidator.Sanitize(code);

        Assert.DoesNotContain("using IAW.Agents.LLM;", result.Code);
        Assert.Contains("using IAW.Agents.System;", result.Code);
        Assert.Contains("IAW.Agents.LLM", result.RemovedUsings);
    }

    [Fact]
    public void Sanitize_RemovesMultipleInvalidNamespaces()
    {
        var code = """
            using IAW.Agents.AI;
            using IAW.Agents.Tools;
            using IAW.Agents.Contracts;
            using IAW.Agents.Coding;
            """;

        var result = CodeValidator.Sanitize(code);

        Assert.Equal(3, result.RemovedUsings.Count);
        Assert.Contains("using IAW.Agents.Coding;", result.Code);
    }

    [Fact]
    public void Sanitize_FixesPartialQualifiers()
    {
        var code = """
            using IAW.Agents.Models;

            var gpt = iaw.Get<Models.IGpt4o>(taskId);
            var shell = iaw.Get<System.IShell>(taskId);
            """;

        var result = CodeValidator.Sanitize(code);

        Assert.DoesNotContain("Models.IGpt4o", result.Code);
        Assert.Contains("iaw.Get<IGpt4o>", result.Code);
        Assert.DoesNotContain("System.IShell", result.Code);
        Assert.Contains("iaw.Get<IShell>", result.Code);
        Assert.Equal(2, result.Fixes.Count);
    }

    [Fact]
    public void Sanitize_PreservesValidCode()
    {
        var code = """
            using System.Text.Json;
            using Aspire.IAW;
            using Core;
            using Core.Contracts;
            using IAW.Agents.System;
            using IAW.Agents.Coding;

            await using var iaw = await IAWCluster.Connect(args);
            var shell = iaw.Get<IShell>(taskId);
            """;

        var result = CodeValidator.Sanitize(code);

        Assert.Empty(result.RemovedUsings);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Validate_DetectsMissingBoilerplate()
    {
        var code = """
            using System;
            Console.WriteLine("no boilerplate");
            """;

        var result = CodeValidator.Validate(code);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Contains("IAWCluster.Connect"));
    }

    [Fact]
    public void Validate_DetectsMissingResultJson()
    {
        var code = """
            await using var iaw = await IAWCluster.Connect(args);
            var taskId = iaw.TaskId;
            Console.WriteLine("done");
            """;

        var result = CodeValidator.Validate(code);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Contains("result.json"));
    }

    [Fact]
    public void Validate_AcceptsCorrectCode()
    {
        var code = """
            using System.Text.Json;
            using Aspire.IAW;
            using Core;
            using Core.Contracts;
            using IAW.Agents.System;

            await using var iaw = await IAWCluster.Connect(args);
            var taskId = iaw.TaskId;
            var shell = iaw.Get<IShell>(taskId);
            File.WriteAllText("result.json", "{}");
            """;

        var result = CodeValidator.Validate(code);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AvailableTypesHint_ContainsKeyInterfaces()
    {
        var hint = CodeValidator.AvailableTypesHint;

        Assert.Contains("IShell", hint);
        Assert.Contains("IRoslyn", hint);
        Assert.Contains("IAspire", hint);
        Assert.Contains("INVALID", hint);
    }
}
