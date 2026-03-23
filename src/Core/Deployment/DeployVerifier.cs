using System.Text.RegularExpressions;

namespace Core.Deployment;

public static partial class DeployVerifier
{
    public static BuildVerification VerifyBuildOutput(string buildOutput)
    {
        if (string.IsNullOrWhiteSpace(buildOutput))
            return new BuildVerification(false, -1, "No build output");

        var errorMatch = ErrorCountRegex().Match(buildOutput);
        var errors = errorMatch.Success ? int.Parse(errorMatch.Groups[1].Value) : -1;
        var succeeded = buildOutput.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase) && errors == 0;

        return new BuildVerification(succeeded, errors, buildOutput);
    }

    public static bool ShouldRevert(BuildVerification verification)
        => !verification.Success;

    [GeneratedRegex(@"(\d+)\s+Error\(s\)")]
    private static partial Regex ErrorCountRegex();
}

public record BuildVerification(bool Success, int Errors, string Output);
