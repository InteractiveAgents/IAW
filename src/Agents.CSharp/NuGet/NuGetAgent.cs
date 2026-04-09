using Core.Contracts;
using IAW.Agents.Coding.Models;
using IAW.Core;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Xml.Linq;

namespace IAW.Agents.Coding;

public class NuGetAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    IHttpClientFactory httpClientFactory)
    : Agent<INuGet>(durableState, chatClient), INuGet
{
    public async Task WatchPackagesAsync(string directoryPackagesPropsPath, TimeSpan checkEvery, CancellationToken ct = default)
    {
        State["props-path"] = new StateEntry("props-path", directoryPackagesPropsPath);
        State["interval-ticks"] = new StateEntry("interval-ticks", checkEvery.Ticks);
        await WriteStateAsync(ct);

        await ScheduleRecurringJob("check-packages", checkEvery, "check-packages", ct);
    }

    public Task<IReadOnlyList<PackageUpdate>> GetOutdatedAsync(CancellationToken ct = default)
    {
        var updates = new List<PackageUpdate>();

        foreach (var kvp in State)
        {
            if (kvp.Key.StartsWith("outdated:", StringComparison.Ordinal))
            {
                var packageId = kvp.Key["outdated:".Length..];
                var parts = kvp.Value.Value.ToString()!.Split('|');
                if (parts.Length == 2)
                    updates.Add(new PackageUpdate(packageId, parts[0], parts[1]));
            }
        }

        return Task.FromResult<IReadOnlyList<PackageUpdate>>(updates);
    }

    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        if (job.Name != "check-packages")
        {
            await base.OnScheduledJobDueAsync(job, ct);
            return;
        }

        if (!State.TryGetValue("props-path", out var pathEntry))
            return;

        var propsPath = pathEntry.Value.ToString()!;
        var packages = ParseDirectoryPackagesProps(propsPath);
        var http = httpClientFactory.CreateClient();

        foreach (var (packageId, currentVersion) in packages)
        {
            try
            {
                var latestVersion = await GetLatestVersionAsync(http, packageId, default);
                if (latestVersion is not null && latestVersion != currentVersion)
                {
                    State[$"outdated:{packageId}"] = new StateEntry(
                        $"outdated:{packageId}", $"{currentVersion}|{latestVersion}");

                    await PublishAsync("dependency.updated", new Dictionary<string, string>
                    {
                        ["PackageId"] = packageId,
                        ["CurrentVersion"] = currentVersion,
                        ["LatestVersion"] = latestVersion
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
            }
        }

        ScheduledJobs[job.Name] = job with { LastRunAt = DateTimeOffset.UtcNow };
        await WriteStateAsync();
    }

    internal static List<(string PackageId, string Version)> ParseDirectoryPackagesProps(string path)
    {
        if (!File.Exists(path))
            return [];

        var doc = XDocument.Load(path);
        return [.. doc.Descendants("PackageVersion")
            .Select(e => (
                PackageId: e.Attribute("Include")?.Value ?? string.Empty,
                Version: e.Attribute("Version")?.Value ?? string.Empty))
            .Where(p => !string.IsNullOrEmpty(p.PackageId) && !string.IsNullOrEmpty(p.Version))];
    }

    internal static async Task<string?> GetLatestVersionAsync(
        HttpClient http, string packageId, CancellationToken ct)
    {
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
        var response = await http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("versions", out var versions))
            return null;

        var allVersions = versions.EnumerateArray()
            .Select(v => v.GetString())
            .Where(v => v is not null && !v.Contains('-'))
            .ToList();

        return allVersions.Count > 0 ? allVersions[^1] : null;
    }
}