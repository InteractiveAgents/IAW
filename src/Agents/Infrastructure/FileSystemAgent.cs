using System.Text.Json;
using Core.AI;
using Core.Contracts;
using Core.Tools;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.System;

public class FileSystemAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient)
    : Agent<IFileSystem>(durableState, chatClient), IFileSystem
{

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
        var resolvedPath = ResolvePathAgainstWorkspace(path);

        var content = await File.ReadAllTextAsync(resolvedPath, ct);

        IncrementFileAccessCount(resolvedPath);
        IncrementCounter("total-reads");
        State["last-access"] = new StateEntry("last-access", DateTimeOffset.UtcNow.ToString("O"));
        await WriteStateAsync(ct);

        await PublishAsync("file.read", new Dictionary<string, string>
        {
            ["Path"] = resolvedPath,
            ["SizeBytes"] = content.Length.ToString()
        }, ct);

        return content;
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken ct = default)
    {
        var resolvedPath = ResolvePathAgainstWorkspace(path);

        var fileExisted = File.Exists(resolvedPath);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (directory is not null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(resolvedPath, content, ct);

        IncrementFileAccessCount(resolvedPath);
        IncrementCounter("total-writes");
        State["last-access"] = new StateEntry("last-access", DateTimeOffset.UtcNow.ToString("O"));
        await WriteStateAsync(ct);

        var eventName = fileExisted ? "file.written" : "file.created";
        await PublishAsync(eventName, new Dictionary<string, string>
        {
            ["Path"] = resolvedPath,
            ["SizeBytes"] = content.Length.ToString()
        }, ct);
    }

    public async Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default)
    {
        var resolvedDir = ResolvePathAgainstWorkspace(directory);
        return await WorkspaceFiles.EnumerateFilesAsync(resolvedDir, pattern, ct);
    }

    public async Task<string[]> SearchCodeAsync(string pattern, string directory, string fileFilter = "*.cs", CancellationToken ct = default)
    {
        var resolvedDir = ResolvePathAgainstWorkspace(directory);

        var files = await WorkspaceFiles.EnumerateFilesAsync(resolvedDir, fileFilter, ct);

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
        var resolvedDirA = ResolvePathAgainstWorkspace(dirA);
        var resolvedDirB = ResolvePathAgainstWorkspace(dirB);

        var comparison = await WorkspaceFiles.CompareDirectoriesAsync(resolvedDirA, resolvedDirB, ct);

        await PublishAsync("directories.compared", new Dictionary<string, string>
        {
            ["DirA"] = resolvedDirA,
            ["DirB"] = resolvedDirB,
            ["OnlyInFirst"] = comparison.OnlyInFirst.Length.ToString(),
            ["OnlyInSecond"] = comparison.OnlyInSecond.Length.ToString(),
            ["Different"] = comparison.DifferentFiles.Length.ToString(),
            ["Identical"] = comparison.IdenticalFiles.Length.ToString()
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
