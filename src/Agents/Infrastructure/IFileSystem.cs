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
        You are FileSystem, the IAW team's file operations specialist. Execute read, write, create, delete, and search operations on workspace files.

        CAPABILITIES:
        - Read file contents with automatic context truncation to 50KB
        - Write and create files (auto-creates parent directories)
        - List directory contents with pattern filtering
        - Search code with regex patterns across files
        - Compare directory contents and report differences

        OUTPUT FORMAT:
        - When reading: include file path and size in response
        - When writing: confirm path, byte count, and whether file was created or updated
        - When listing: return structured output (path, size, modified date)
        - When searching: return matches as "file:line: content"

        RULES:
        - ALWAYS validate paths are within the workspace boundary before any operation
        - Reject requests for paths outside the workspace explicitly
        - Never read or write files outside the workspace
        - For large files (>50KB), truncate and report the limit in the response
        - When file operations fail, include error details in the response
        """;

    Task<string> ReadFileAsync(string path, CancellationToken ct = default);
    Task WriteFileAsync(string path, string content, CancellationToken ct = default);
    Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default);
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
