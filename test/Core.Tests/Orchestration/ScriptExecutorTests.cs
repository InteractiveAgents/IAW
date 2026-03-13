using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class ScriptExecutorTests
{
    [Fact]
    public async Task ExecuteScriptAsync_with_failing_validator_returns_error()
    {
        var executor = new ScriptExecutor();
        var result = await executor.ExecuteScriptAsync(
            "invalid code",
            Path.GetTempPath(),
            source => (false, ["syntax error at line 1"]),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("syntax error", result.Output);
        Assert.Equal("Compilation validation failed", result.Error);
    }

    [Fact]
    public async Task ExecuteScriptAsync_with_multiple_validation_errors_joins_them()
    {
        var executor = new ScriptExecutor();
        var result = await executor.ExecuteScriptAsync(
            "bad code",
            Path.GetTempPath(),
            source => (false, ["error one", "error two"]),
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("error one", result.Output);
        Assert.Contains("error two", result.Output);
        Assert.Equal("Compilation validation failed", result.Error);
    }

    [Fact]
    public async Task ExecuteScriptAsync_without_validator_has_no_error()
    {
        var executor = new ScriptExecutor();
        // use a temp dir that will fail at scaffold, but no NRE from null validator
        var tempDir = Path.Combine(Path.GetTempPath(), $"se-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = await executor.ExecuteScriptAsync(
                "Console.WriteLine(\"hello\");",
                tempDir,
                ct: TestContext.Current.CancellationToken);

            Assert.Null(result.Error);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void ScriptResult_success_is_true_when_exit_code_zero()
    {
        var result = new ScriptResult(0, "ok");
        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ScriptResult_success_is_false_when_exit_code_nonzero()
    {
        var result = new ScriptResult(1, "fail") { Error = "some error" };
        Assert.False(result.Success);
        Assert.Equal("some error", result.Error);
    }
}
