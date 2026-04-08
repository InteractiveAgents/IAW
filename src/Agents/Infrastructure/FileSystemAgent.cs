using Core.AI;
using Core.Contracts;
using Core.Services;
using Core.Tools;
using Core.UI;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UIAgentResponse = Core.UI.AgentResponse;

namespace IAW.Agents.System;

public class FileSystemAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient)
    : Agent<IFileSystem>(durableState, chatClient), IFileSystem
{
    readonly List<MediaPart> _pendingDeliveries = [];

    public override async Task<UIAgentResponse> GetRichResponse(string prompt, CancellationToken ct = default)
    {
        _pendingDeliveries.Clear();
        var text = await GetResponse(prompt, ct);
        var parts = new List<UIPart> { new TextPart(text) };
        parts.AddRange(_pendingDeliveries);
        _pendingDeliveries.Clear();
        return new UIAgentResponse(parts);
    }

    protected override IReadOnlyList<AITool> DefineTools()
    {
        Func<string> workspace = () => GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new FileTools(workspace));
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

    public async Task<string> CopyAsync(string source, string destination, CancellationToken ct = default)
    {
        var resolvedSource = ResolvePathAgainstWorkspace(source);
        var resolvedDest = ResolvePathAgainstWorkspace(destination);

        if (File.Exists(resolvedSource))
        {
            var destDir = Path.GetDirectoryName(resolvedDest);
            if (destDir is not null && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(resolvedSource, resolvedDest, overwrite: true);
        }
        else if (Directory.Exists(resolvedSource))
        {
            CopyDirectoryRecursive(resolvedSource, resolvedDest);
        }
        else
        {
            return $"Source not found: {resolvedSource}";
        }

        IncrementCounter("total-writes");
        await WriteStateAsync(ct);
        await PublishAsync("file.copied", new Dictionary<string, string>
        {
            ["Source"] = resolvedSource,
            ["Destination"] = resolvedDest
        }, ct);

        return $"Copied {resolvedSource} -> {resolvedDest}";
    }

    public async Task<string> MoveAsync(string source, string destination, CancellationToken ct = default)
    {
        var resolvedSource = ResolvePathAgainstWorkspace(source);
        var resolvedDest = ResolvePathAgainstWorkspace(destination);

        if (File.Exists(resolvedSource))
        {
            var destDir = Path.GetDirectoryName(resolvedDest);
            if (destDir is not null && !Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Move(resolvedSource, resolvedDest, overwrite: true);
        }
        else if (Directory.Exists(resolvedSource))
        {
            Directory.Move(resolvedSource, resolvedDest);
        }
        else
        {
            return $"Source not found: {resolvedSource}";
        }

        IncrementCounter("total-writes");
        await WriteStateAsync(ct);
        await PublishAsync("file.moved", new Dictionary<string, string>
        {
            ["Source"] = resolvedSource,
            ["Destination"] = resolvedDest
        }, ct);

        return $"Moved {resolvedSource} -> {resolvedDest}";
    }

    public async Task<string> DeleteAsync(string path, CancellationToken ct = default)
    {
        var resolvedPath = ResolvePathAgainstWorkspace(path);

        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var fileSize = new FileInfo(resolvedPath).Length;
        File.Delete(resolvedPath);

        IncrementCounter("total-writes");
        await WriteStateAsync(ct);
        await PublishAsync("file.deleted", new Dictionary<string, string>
        {
            ["Path"] = resolvedPath,
            ["SizeBytes"] = fileSize.ToString()
        }, ct);

        return $"Deleted {resolvedPath} ({fileSize:N0} bytes)";
    }

    public Task<string> GetInfoAsync(string path, CancellationToken ct = default)
    {
        var resolvedPath = ResolvePathAgainstWorkspace(path);

        if (File.Exists(resolvedPath))
        {
            var info = new FileInfo(resolvedPath);
            return Task.FromResult(
                $"File: {info.FullName}\n" +
                $"Size: {info.Length:N0} bytes\n" +
                $"Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Accessed: {info.LastAccessTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"ReadOnly: {info.IsReadOnly}\n" +
                $"Extension: {info.Extension}");
        }

        if (Directory.Exists(resolvedPath))
        {
            var info = new DirectoryInfo(resolvedPath);
            var fileCount = info.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Count();
            var dirCount = info.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).Count();
            return Task.FromResult(
                $"Directory: {info.FullName}\n" +
                $"Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Files: {fileCount}\n" +
                $"Subdirectories: {dirCount}");
        }

        return Task.FromResult($"Not found: {resolvedPath}");
    }

    public async Task<string> ReadLinesAsync(string path, int startLine, int count, CancellationToken ct = default)
    {
        var resolvedPath = ResolvePathAgainstWorkspace(path);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var sb = new StringBuilder();
        var lineNumber = 0;
        var linesRead = 0;

        using var reader = new StreamReader(resolvedPath);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;
            if (lineNumber < startLine) continue;
            if (linesRead >= count) break;

            sb.AppendLine($"{lineNumber}: {line}");
            linesRead++;
        }

        IncrementCounter("total-reads");
        await WriteStateAsync(ct);

        if (linesRead == 0)
            return $"No lines in range. File has {lineNumber} lines total.";

        return sb.ToString();
    }

    public async Task<string> CreateArchiveAsync(string outputPath, string sourcePath, CancellationToken ct = default)
    {
        var resolvedOutput = ResolvePathAgainstWorkspace(outputPath);
        var resolvedSource = ResolvePathAgainstWorkspace(sourcePath);

        if (!Directory.Exists(resolvedSource))
            return $"Source directory not found: {resolvedSource}";

        var outputDir = Path.GetDirectoryName(resolvedOutput);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        if (File.Exists(resolvedOutput))
            File.Delete(resolvedOutput);

        ZipFile.CreateFromDirectory(resolvedSource, resolvedOutput);

        var archiveSize = new FileInfo(resolvedOutput).Length;
        IncrementCounter("total-writes");
        await WriteStateAsync(ct);
        await PublishAsync("archive.created", new Dictionary<string, string>
        {
            ["OutputPath"] = resolvedOutput,
            ["SourcePath"] = resolvedSource,
            ["SizeBytes"] = archiveSize.ToString()
        }, ct);

        return $"Archive created: {resolvedOutput} ({archiveSize:N0} bytes)";
    }

    public async Task<string> ExtractArchiveAsync(string archivePath, string destinationPath, CancellationToken ct = default)
    {
        var resolvedArchive = ResolvePathAgainstWorkspace(archivePath);
        var resolvedDest = ResolvePathAgainstWorkspace(destinationPath);

        if (!File.Exists(resolvedArchive))
            return $"Archive not found: {resolvedArchive}";

        if (!Directory.Exists(resolvedDest))
            Directory.CreateDirectory(resolvedDest);

        ZipFile.ExtractToDirectory(resolvedArchive, resolvedDest, overwriteFiles: true);

        var extractedFiles = Directory.GetFiles(resolvedDest, "*", SearchOption.AllDirectories);
        IncrementCounter("total-writes");
        await WriteStateAsync(ct);
        await PublishAsync("archive.extracted", new Dictionary<string, string>
        {
            ["ArchivePath"] = resolvedArchive,
            ["DestinationPath"] = resolvedDest,
            ["EntryCount"] = extractedFiles.Length.ToString()
        }, ct);

        return $"Extracted {extractedFiles.Length} files to {resolvedDest}";
    }

    public async Task<string> UploadFileAsync(string path, CancellationToken ct = default)
    {
        var resolvedPath = ResolvePathAgainstWorkspace(path);
        if (!File.Exists(resolvedPath))
            return $"File not found: {resolvedPath}";

        var blobStorage = ServiceProvider.GetRequiredService<BlobFileStorage>();
        var fileName = Path.GetFileName(resolvedPath);
        var mimeType = MimeTypes.GetMimeType(fileName);
        var blobPath = $"deliveries/{Guid.NewGuid():N}/{fileName}";

        await using var stream = File.OpenRead(resolvedPath);
        var blobUrl = await blobStorage.UploadAsync(stream, blobPath, mimeType);

        IncrementCounter("total-reads");
        await WriteStateAsync(ct);
        _pendingDeliveries.Add(new MediaPart(blobUrl, fileName, mimeType, fileName));

        await PublishAsync("file.uploaded", new Dictionary<string, string>
        {
            ["Path"] = resolvedPath,
            ["BlobUrl"] = blobUrl,
            ["FileName"] = fileName,
            ["MimeType"] = mimeType,
            ["SizeBytes"] = new FileInfo(resolvedPath).Length.ToString()
        }, ct);

        return blobUrl;
    }

    static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
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