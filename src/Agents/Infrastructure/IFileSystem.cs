using System.ComponentModel;
using Core.Contracts;
using Core.Tools;

namespace IAW.Agents.System;

public interface IFileSystem : IAgent
{
    static string IAgent.AgentDisplayName => "FileSystem";

    static string IAgent.AgentDescription =>
        "Reads, writes, lists, and searches workspace files with boundary validation and structured output.";

    static string[] IAgent.AgentCapabilities =>
        ["file", "read", "write", "search", "filesystem", "workspace"];

    static string IAgent.AgentInstructions => """
        You are FileSystem, the file operations specialist. You read, write, list,
        and search files anywhere on the PC.

        RULES:
        - Execute file operations immediately — never give manual instructions.
        - Absolute paths work as-is. Relative paths resolve against workspace if set.
        - No path restrictions — you have full access to the entire filesystem.
        - Truncate file contents to 50KB when reading large files. Note truncation.
        - When writing, auto-create parent directories.
        - DO NOT analyze code — use Roslyn for that. DO NOT build — use DotNet.

        TOOLS: ReadFile, WriteFile, ListFiles, SearchCode, CompareDirectories.
        """;

    [Description("Read a file's contents from any path on the PC. Truncates to 50KB for large files.")]
    Task<string> ReadFileAsync(string path, CancellationToken ct = default);

    [Description("Write content to a file at any path. Creates the file and parent directories if they don't exist.")]
    Task WriteFileAsync(string path, string content, CancellationToken ct = default);

    [Description("List files in a directory matching a glob pattern. Default pattern '*' lists all. Returns array of file paths.")]
    Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default);

    [Description("Search for a regex pattern across files in a directory. Returns matching lines as 'file:line: content'.")]
    Task<string[]> SearchCodeAsync(string pattern, string directory, string fileFilter = "*.cs", CancellationToken ct = default);

    Task<DirectoryComparison> CompareDirectoriesAsync(string dirA, string dirB, CancellationToken ct = default);
    Task<FileAccessMetrics> GetMetricsAsync(CancellationToken ct = default);
}

[GenerateSerializer]
public record FileAccessMetrics(
    [property: Id(0)] int TotalReads,
    [property: Id(1)] int TotalWrites,
    [property: Id(2)] Dictionary<string, int> FileAccessCounts,
    [property: Id(3)] DateTimeOffset LastAccess);
