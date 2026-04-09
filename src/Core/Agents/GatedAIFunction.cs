using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IAW.Core;

public sealed partial class GatedAIFunction(AIFunction inner, Func<string, string, CancellationToken, Task<GateResult>> gate) : AIFunction
{
    public override string Name => inner.Name;
    public override string Description => inner.Description;
    public override JsonElement JsonSchema => inner.JsonSchema;
    public override System.Reflection.MethodInfo? UnderlyingMethod => inner.UnderlyingMethod;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var preview = BuildArgumentsPreview(arguments);
        var result = await gate(inner.Name, preview, cancellationToken);
        if (!result.Allowed)
            return result.DenyMessage;

        return await inner.InvokeAsync(arguments, cancellationToken);
    }

    static string BuildArgumentsPreview(AIFunctionArguments arguments)
    {
        if (arguments.Count == 0)
            return "(no arguments)";

        try
        {
            var dict = arguments.ToDictionary(kv => kv.Key, kv => kv.Value);
            var json = JsonSerializer.Serialize(dict);
            var redacted = RedactSecrets(json);
            return redacted.Length > 500 ? redacted[..500] + "...(truncated)" : redacted;
        }
        catch
        {
            return $"({arguments.Count} argument(s))";
        }
    }

    static string RedactSecrets(string input)
    {
        var redacted = SecretLikeKeyValue().Replace(input, m => $"{m.Groups[1].Value}\":\"[REDACTED]\"");
        redacted = BearerToken().Replace(redacted, "Bearer [REDACTED]");
        return redacted;
    }

    [GeneratedRegex(@"""([^""]*(?:api[_-]?key|token|password|secret|authorization|bearer)[^""]*)""\s*:\s*""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex SecretLikeKeyValue();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/=]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();
}

public readonly record struct GateResult(bool Allowed, string DenyMessage)
{
    public static GateResult Allow() => new(true, "");
    public static GateResult Deny(string reason) => new(false, $"[Action blocked by user/security policy: {reason}]");
}
