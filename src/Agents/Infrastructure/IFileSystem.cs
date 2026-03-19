using Core.Contracts;
using Core.Tools;

namespace IAW.Agents.System;

public interface IFileSystem : IAgent
{
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
