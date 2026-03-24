using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Core.Tools;

// git-aware file enumeration + token-efficient comparison
public static class WorkspaceFiles
{
    private static readonly HashSet<string> FallbackExclusions =
    [
        with(StringComparer.OrdinalIgnoreCase), ".git", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults", ".claude", "packages"
    ];

    public static async Task<string[]> EnumerateFilesAsync(
        string directory, string pattern = "*", CancellationToken ct = default)
    {
        if (!Directory.Exists(directory))
            return [];

        var gitRoot = FindGitRoot(directory);
        if (gitRoot is not null)
            return await EnumerateViaGitAsync(directory, gitRoot, pattern, ct);

        return [.. EnumerateWithExclusions(directory, pattern)];
    }

    public static async Task<DirectoryComparison> CompareDirectoriesAsync(
        string dirA, string dirB, CancellationToken ct = default)
    {
        var filesA = (await EnumerateFilesAsync(dirA, "*", ct))
            .ToDictionary(f => Path.GetRelativePath(dirA, f), f => f, StringComparer.OrdinalIgnoreCase);
        var filesB = (await EnumerateFilesAsync(dirB, "*", ct))
            .ToDictionary(f => Path.GetRelativePath(dirB, f), f => f, StringComparer.OrdinalIgnoreCase);

        var onlyInA = filesA.Keys.Except(filesB.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        var onlyInB = filesB.Keys.Except(filesA.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        var common = filesA.Keys.Intersect(filesB.Keys, StringComparer.OrdinalIgnoreCase).ToArray();

        var identical = new List<string>();
        var different = new List<FileDifference>();

        foreach (var relativePath in common)
        {
            ct.ThrowIfCancellationRequested();

            var infoA = new FileInfo(filesA[relativePath]);
            var infoB = new FileInfo(filesB[relativePath]);

            // fast path: different size = definitely different, skip reading content
            if (infoA.Length != infoB.Length)
            {
                different.Add(new FileDifference(relativePath, infoA.Length, infoB.Length));
                continue;
            }

            // same size: hash comparison instead of reading full content into memory
            if (await HashesMatchAsync(filesA[relativePath], filesB[relativePath], ct))
                identical.Add(relativePath);
            else
                different.Add(new FileDifference(relativePath, infoA.Length, infoB.Length));
        }

        return new DirectoryComparison(onlyInA, onlyInB, [.. different], [.. identical]);
    }

    static async Task<string[]> EnumerateViaGitAsync(
        string directory, string gitRoot, string pattern, CancellationToken ct)
    {
        // git ls-files returns tracked + untracked-but-not-ignored files
        var relativeTo = Path.GetRelativePath(gitRoot, directory);
        var prefix = relativeTo == "." ? "" : relativeTo.Replace('\\', '/') + "/";

        var args = string.IsNullOrEmpty(prefix)
            ? "ls-files --cached --others --exclude-standard"
            : $"ls-files --cached --others --exclude-standard -- \"{prefix}\"";

        var output = await RunGitAsync(gitRoot, args, ct);
        if (output is null)
            return [.. EnumerateWithExclusions(directory, pattern)];

        var matcher = CreatePatternMatcher(pattern);

        return [.. output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => matcher(Path.GetFileName(line)))
            .Select(line => Path.GetFullPath(Path.Combine(gitRoot, line)))
            .Where(File.Exists)];
    }

    static async Task<string?> RunGitAsync(string workingDir, string args, CancellationToken ct)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? stdout : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static IEnumerable<string> EnumerateWithExclusions(string directory, string pattern)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true };

        foreach (var file in Directory.EnumerateFiles(directory, pattern, options))
            yield return file;

        foreach (var subDir in Directory.EnumerateDirectories(directory, "*", options))
        {
            if (FallbackExclusions.Contains(Path.GetFileName(subDir)))
                continue;

            foreach (var file in EnumerateWithExclusions(subDir, pattern))
                yield return file;
        }
    }

    static string? FindGitRoot(string directory)
    {
        var current = Path.GetFullPath(directory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
                return current;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    static async Task<bool> HashesMatchAsync(string pathA, string pathB, CancellationToken ct)
    {
        var hashA = await ComputeHashAsync(pathA, ct);
        var hashB = await ComputeHashAsync(pathB, ct);
        return hashA.SequenceEqual(hashB);
    }

    static async Task<byte[]> ComputeHashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream, ct);
    }

    static Func<string, bool> CreatePatternMatcher(string pattern)
    {
        if (pattern == "*") return _ => true;

        // handle simple *.ext patterns
        if (pattern.StartsWith("*."))
        {
            var ext = pattern[1..]; // ".ext"
            return fileName => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        var regexSource = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var compiled = new Regex(regexSource, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return fileName => compiled.IsMatch(fileName);
    }
}

[GenerateSerializer]
public record DirectoryComparison(
    [property: Id(0)] string[] OnlyInFirst,
    [property: Id(1)] string[] OnlyInSecond,
    [property: Id(2)] FileDifference[] DifferentFiles,
    [property: Id(3)] string[] IdenticalFiles);

[GenerateSerializer]
public record FileDifference(
    [property: Id(0)] string RelativePath,
    [property: Id(1)] long SizeA,
    [property: Id(2)] long SizeB);