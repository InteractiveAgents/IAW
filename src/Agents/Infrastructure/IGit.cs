using Core.Contracts;
using System.ComponentModel;

namespace IAW.Agents.Coding;

public interface IGit : IAgent
{
    static string IAgent.AgentDisplayName => "Git";

    static string IAgent.AgentDescription =>
        "Manages git version control operations including commits, branches, diffs, and repository history.";

    static string[] IAgent.AgentCapabilities =>
        ["git", "commit", "branch", "diff", "version-control", "repository"];

    static string IAgent.AgentInstructions => """
        You are Git, the version control specialist. You manage commits, branches,
        diffs, and repository state.

        RULES:
        - Execute git operations immediately — never give manual instructions.
        - Always run Status before Commit to verify staged changes.
        - Write commit messages in imperative mood, max 72 characters for subject.
        - Never force-push or rewrite public history.
        - For merge conflicts, report conflicting files and let the user decide.
        - DO NOT modify file contents — use FileSystem agent for that.

        TOOLS: Status, Commit, Diff, Log, Revert.
        """;

    [Description("Show git status of a repository. Returns branch name, staged/unstaged/untracked files.")]
    Task<string> StatusAsync(string repoPath, CancellationToken ct = default);

    [Description("Create a git commit with a message. Stage files first if needed. Returns commit hash and message.")]
    Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default);

    [Description("Show git diff of unstaged changes in a repository. Returns file paths and line changes.")]
    Task<string> DiffAsync(string repoPath, CancellationToken ct = default);

    [Description("Show git log of recent commits. Returns hash, author, subject per commit. Default 10 entries.")]
    Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default);

    [Description("Revert a specific git commit by hash. Returns result message.")]
    Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default);

    Task<GitMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record GitMetrics(
    [property: Id(0)] int TotalCommits,
    [property: Id(1)] int TotalReverts,
    [property: Id(2)] Dictionary<string, int> FileChurn,
    [property: Id(3)] DateTimeOffset LastCommit);