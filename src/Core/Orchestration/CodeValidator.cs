namespace Core.Orchestration;

public static class CodeValidator
{
    static readonly HashSet<string> ValidIawNamespaces =
    [
        "IAW.Agents.System",
        "IAW.Agents.Coding",
        "IAW.Agents.Infrastructure",
        "IAW.Agents.Memory",
        "IAW.Agents.Orchestration",
        "IAW.Agents.Models",
        "IAW.Agents.Messages"
    ];

    static readonly string[] InvalidNamespacePatterns =
    [
        "IAW.Agents.LLM",
        "IAW.Agents.AI",
        "IAW.Agents.Tools",
        "IAW.Agents.Core",
        "IAW.Agents.Contracts",
        "IAW.Agents.Services",
    ];

    // Partial qualifiers LLMs misuse inside iaw.Get<> calls
    static readonly string[] BadGetQualifiers =
    [
        "Models.",
        "Coding.",
        "Infrastructure.",
        "Memory.",
        "System.",
        "Orchestration.",
    ];

    public static SanitizeResult Sanitize(string code)
    {
        var removedUsings = new List<string>();
        var fixes = new List<string>();
        var lines = code.Split('\n').ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith("using ") && trimmed.EndsWith(';') && !trimmed.Contains('('))
            {
                var ns = trimmed["using ".Length..^1].Trim();
                if (IsInvalidNamespace(ns))
                {
                    removedUsings.Add(ns);
                    lines[i] = "";
                    continue;
                }
            }

            if (lines[i].Contains("iaw.Get<"))
            {
                var original = lines[i];
                foreach (var qualifier in BadGetQualifiers)
                {
                    var pattern = $"iaw.Get<{qualifier}";
                    if (lines[i].Contains(pattern))
                        lines[i] = lines[i].Replace(pattern, "iaw.Get<");
                }
                if (lines[i] != original)
                    fixes.Add($"Fixed qualifier in: {original.Trim()}");
            }
        }

        var result = string.Join('\n', lines);
        while (result.Contains("\n\n\n"))
            result = result.Replace("\n\n\n", "\n\n");

        return new SanitizeResult(result, removedUsings, fixes);
    }

    public static ValidationResult Validate(string code)
    {
        var issues = new List<string>();

        if (!code.Contains("IAWCluster.Connect"))
            issues.Add("Missing required boilerplate: await using var iaw = await IAWCluster.Connect(args);");

        if (!code.Contains("result.json"))
            issues.Add("Missing result.json output — generated code must write result.json");

        return new ValidationResult(issues.Count == 0, issues);
    }

    static bool IsInvalidNamespace(string ns)
    {
        foreach (var pattern in InvalidNamespacePatterns)
            if (ns.Equals(pattern, StringComparison.Ordinal))
                return true;

        if (ns.StartsWith("IAW.") && !ValidIawNamespaces.Any(ns.StartsWith))
            return true;

        return false;
    }

    public static string AvailableTypesHint => """
        VALID NAMESPACES AND INTERFACES (use ONLY these):
          IAW.Agents.System        → IShell, IFileSystem
          IAW.Agents.Coding        → IGit, IRoslyn, IDotNet, INuGet, IGitHub
          IAW.Agents.Infrastructure → IAspire
          IAW.Agents.Memory        → ICodeMemory, IUserMemory, IProjectMemory, IEpisodeMemory, IPatternMemory, IKnowledge
          IAW.Agents.Orchestration → IThread
          IAW.Agents.Models        → (NO interfaces — LLM agents have no public interfaces, do NOT use them)
          Core.Contracts           → IAgent, ICodeOrchestrator, IMemoryAgent

        INVALID (do NOT use): IAW.Agents.LLM, IAW.Agents.AI, IAW.Agents.Tools, Models.IXxx qualifiers
        Always use interface names directly after importing the namespace: iaw.Get<IShell>(taskId), NOT iaw.Get<System.IShell>(taskId)
        """;
}

public record SanitizeResult(string Code, List<string> RemovedUsings, List<string> Fixes);
public record ValidationResult(bool IsValid, List<string> Issues);
