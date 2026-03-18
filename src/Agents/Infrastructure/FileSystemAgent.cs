using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Tools;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Infrastructure;

public class FileSystemAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IFileSystem
{
    protected override string DisplayName => "FileSystem";
    protected override string Instructions => """
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

    protected override IReadOnlyList<AITool> DefineTools()
    {
        Func<string> workspace = () => GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new FileTools(workspace));
        RegisterToolMethods(tools, new ShellTools(workspace));
        return tools;
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(path);

        var content = await File.ReadAllTextAsync(path, ct);

        IncrementFileAccessCount(path);
        IncrementCounter("total-reads");
        State["last-access"] = new StateEntry("last-access", DateTimeOffset.UtcNow.ToString("O"));
        await WriteStateAsync(ct);

        await PublishAsync("file.read", new Dictionary<string, object>
        {
            ["Path"] = path,
            ["SizeBytes"] = content.Length
        }, ct);

        return content;
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(path);

        var fileExisted = File.Exists(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content, ct);

        IncrementFileAccessCount(path);
        IncrementCounter("total-writes");
        State["last-access"] = new StateEntry("last-access", DateTimeOffset.UtcNow.ToString("O"));
        await WriteStateAsync(ct);

        var eventName = fileExisted ? "file.written" : "file.created";
        await PublishAsync(eventName, new Dictionary<string, object>
        {
            ["Path"] = path,
            ["SizeBytes"] = content.Length
        }, ct);
    }

    public async Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(directory);
        return await WorkspaceFiles.EnumerateFilesAsync(directory, pattern, ct);
    }

    public async Task<string[]> SearchCodeAsync(string pattern, string directory, string fileFilter = "*.cs", CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(directory);

        var files = await WorkspaceFiles.EnumerateFilesAsync(directory, fileFilter, ct);

        var matchingLines = new List<string>();
        foreach (var file in files)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            for (var lineNum = 0; lineNum < lines.Length; lineNum++)
            {
                if (lines[lineNum].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    matchingLines.Add($"{file}:{lineNum + 1}: {lines[lineNum].TrimStart()}");
            }
        }
        return [.. matchingLines];
    }

    public async Task<DirectoryComparison> CompareDirectoriesAsync(string dirA, string dirB, CancellationToken ct = default)
    {
        ValidatePathWithinWorkspace(dirA);
        ValidatePathWithinWorkspace(dirB);

        var comparison = await WorkspaceFiles.CompareDirectoriesAsync(dirA, dirB, ct);

        await PublishAsync("directories.compared", new Dictionary<string, object>
        {
            ["DirA"] = dirA,
            ["DirB"] = dirB,
            ["OnlyInFirst"] = comparison.OnlyInFirst.Length,
            ["OnlyInSecond"] = comparison.OnlyInSecond.Length,
            ["Different"] = comparison.DifferentFiles.Length,
            ["Identical"] = comparison.IdenticalFiles.Length
        }, ct);

        return comparison;
    }

    public Task<FileAccessMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalReads = GetCounterValue("total-reads");
        var totalWrites = GetCounterValue("total-writes");
        var fileAccessCounts = GetFileAccessCounts();
        var lastAccess = State.TryGetValue("last-access", out var lastAccessDesc)
            ? DateTimeOffset.Parse(lastAccessDesc.Value.ToString()!)
            : DateTimeOffset.MinValue;

        return Task.FromResult(new FileAccessMetrics(totalReads, totalWrites, fileAccessCounts, lastAccess));
    }

    private void IncrementCounter(string counterKey)
    {
        var current = GetCounterValue(counterKey);
        State[counterKey] = new StateEntry(counterKey, current + 1);
    }

    private int GetCounterValue(string counterKey)
    {
        if (!State.TryGetValue(counterKey, out var desc)) return 0;
        return desc.Value is int i ? i : int.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private void IncrementFileAccessCount(string path)
    {
        var countsKey = "file-access-counts";
        var counts = GetFileAccessCounts();
        counts.TryGetValue(path, out var current);
        counts[path] = current + 1;
        State[countsKey] = new StateEntry(countsKey, JsonSerializer.Serialize(counts));
    }

    private Dictionary<string, int> GetFileAccessCounts()
    {
        if (!State.TryGetValue("file-access-counts", out var desc))
            return new Dictionary<string, int>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(desc.Value.ToString()!)
                   ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }
}
