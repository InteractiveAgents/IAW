using Core.Contracts;
using Core.Tools;
using System.ComponentModel;

namespace IAW.Agents.System;

public interface IFileSystem : IAgent
{
    static string IAgent.AgentDisplayName => "FileSystem";

    static string IAgent.AgentDescription =>
        "Full-featured filesystem agent: read, write, copy, move, delete, archive, search, and upload files from anywhere on the PC.";

    static string[] IAgent.AgentCapabilities =>
        ["file", "read", "write", "copy", "move", "delete", "search", "archive", "upload", "filesystem", "workspace"];

    static string[] IAgent.AgentRoutingExamples =>
        ["send me a file", "deliver the file to me", "zip this folder and send it",
         "upload the document", "search for files containing", "read the contents of",
         "copy files to another folder", "find all .txt files"];

    static string IAgent.AgentInstructions => """
        You are FileSystem, the file operations specialist. You manage files anywhere on the PC.

        RULES:
        - Execute file operations immediately — never give manual instructions.
        - Absolute paths work as-is. Relative paths resolve against workspace if set.
        - No path restrictions — you have full access to the entire filesystem.
        - When writing, auto-create parent directories.
        - For large files, use ReadLines to read specific ranges instead of reading the entire file.
        - Use UploadFile when the user wants to receive a file — it delivers directly to their chat.
        - DO NOT analyze code — use Roslyn for that. DO NOT build — use DotNet.

        TOOLS: ReadFile, WriteFile, ListFiles, SearchCode, CompareDirectories, Copy, Move, Delete, GetInfo, ReadLines, CreateArchive, ExtractArchive, UploadFile.
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

    [Description("Copy a file or directory to a new location. Creates parent directories if needed. Returns confirmation.")]
    Task<string> CopyAsync(string source, string destination, CancellationToken ct = default);

    [Description("Move or rename a file or directory. Returns confirmation.")]
    Task<string> MoveAsync(string source, string destination, CancellationToken ct = default);

    [Description("Delete a file. For safety, does NOT delete directories. Returns confirmation.")]
    Task<string> DeleteAsync(string path, CancellationToken ct = default);

    [Description("Get file or directory metadata: size, creation date, last modified, attributes. Returns formatted info.")]
    Task<string> GetInfoAsync(string path, CancellationToken ct = default);

    [Description("Read a range of lines from a file. Line numbers are 1-based. Use for large files to avoid loading everything.")]
    Task<string> ReadLinesAsync(string path, int startLine, int count, CancellationToken ct = default);

    [Description("Create a zip archive from a directory. Returns the archive path.")]
    Task<string> CreateArchiveAsync(string outputPath, string sourcePath, CancellationToken ct = default);

    [Description("Extract a zip archive to a destination directory. Returns extraction summary.")]
    Task<string> ExtractArchiveAsync(string archivePath, string destinationPath, CancellationToken ct = default);

    [Description("Deliver a file to the user in their current chat. Call once per file. Returns confirmation.")]
    Task<string> UploadFileAsync(string path, CancellationToken ct = default);
}

[GenerateSerializer]
public record FileAccessMetrics(
    [property: Id(0)] int TotalReads,
    [property: Id(1)] int TotalWrites,
    [property: Id(2)] Dictionary<string, int> FileAccessCounts,
    [property: Id(3)] DateTimeOffset LastAccess);