using Core.Contracts;

namespace IAW.Agents.Coding;

public interface IGit : IAgent
{
    static string IAgent.AgentDisplayName => "Git";

    static string IAgent.AgentDescription =>
        "Manages git version control operations including commits, branches, diffs, and repository history.";

    static string[] IAgent.AgentCapabilities =>
        ["git", "commit", "branch", "diff", "version-control", "repository"];

    static string IAgent.AgentInstructions => """
        You are Git, the IAW team's version control specialist. Manage commits, branches, diffs, and repository state.

        CAPABILITIES:
        - View repository status and staged changes
        - Create, switch, merge, and delete branches
        - Commit with descriptive messages
        - Stage specific files or patterns
        - View commit history and detailed diffs
        - Revert commits and stash/unstash changes

        OUTPUT FORMAT:
        - Commit results: "Committed <hash>: <message>"
        - Status: list staged, unstaged, and untracked files
        - Logs: show hash, author, subject in concise format
        - Diffs: show file paths and line changes

        RULES:
        - Always run git status before commits to verify staged changes
        - Write commit messages in imperative mood, max 72 characters for subject line
        - Never force-push or rewrite public history
        - For merge conflicts, report conflicting files and let the user decide resolution
        - Report results concisely; include exit code and error messages on failure
        """;

    Task<string> StatusAsync(string repoPath, CancellationToken ct = default);
    Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default);
    Task<string> DiffAsync(string repoPath, CancellationToken ct = default);
    Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default);
    Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default);
    Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record GitMetrics(
    [property: Id(0)] int TotalCommits,
    [property: Id(1)] int TotalReverts,
    [property: Id(2)] Dictionary<string, int> FileChurn,
    [property: Id(3)] DateTimeOffset LastCommit);
