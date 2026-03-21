using System.Text.Json;
using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests;

public class OrchestrationResultTests
{
    [Fact]
    public void OrchestrationResult_RoundTrips_ViaJson()
    {
        var result = new OrchestrationResult(
            Success: true,
            Summary: "Built successfully",
            WorkspacePath: @"D:\IAW\Calc",
            Artifacts: ["D:\\IAW\\Calc\\App.csproj"],
            Metrics: new() { ["duration"] = "12.4s" },
            ErrorDetail: null,
            TaskId: "2026-03-21-test-task-abc123");

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<OrchestrationResult>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal("Built successfully", deserialized.Summary);
        Assert.Single(deserialized.Artifacts);
        Assert.Null(deserialized.ErrorDetail);
    }

    [Fact]
    public void OrchestrationResult_Failure_PreservesErrorDetail()
    {
        var result = new OrchestrationResult(
            Success: false,
            Summary: "Build failed",
            WorkspacePath: @"D:\workspace\tasks\test",
            Artifacts: [],
            Metrics: null,
            ErrorDetail: "CS1002: ; expected at Form1.cs:42");

        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<OrchestrationResult>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("CS1002: ; expected at Form1.cs:42", deserialized.ErrorDetail);
    }

    [Fact]
    public void OrchestrationResult_Deserialize_FallsBackGracefully()
    {
        var plainText = "This is not JSON";
        OrchestrationResult? parsed = null;
        try { parsed = JsonSerializer.Deserialize<OrchestrationResult>(plainText); }
        catch (JsonException) { }
        Assert.Null(parsed);
    }
}
