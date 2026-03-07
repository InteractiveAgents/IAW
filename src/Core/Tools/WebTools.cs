using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace IAW.Core.Tools;

public class WebTools(HttpClient httpClient)
{
    private static readonly string[] BlockedHostPatterns =
        ["localhost", "127.0.0.1", "::1", "0.0.0.0", "[::1]", "metadata.google", "169.254.169.254"];

    [Description("Fetch content from a URL")]
    public async Task<string> FetchUrlAsync([Description("URL to fetch")] string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return $"Invalid URL: {url}";

            if (!IsAllowedScheme(uri))
                return $"Blocked: only http and https schemes are allowed. Got: {uri.Scheme}";

            if (IsBlockedHost(uri))
                return $"Blocked: requests to internal/localhost addresses are not allowed.";

            if (await ResolvesToPrivateIpAsync(uri))
                return $"Blocked: URL resolves to a private/internal IP address.";

            var response = await httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return content.Length > 50_000 ? content[..50_000] + "\n... (truncated)" : content;
        }
        catch (Exception ex)
        {
            return $"Error fetching {url}: {ex.Message}";
        }
    }

    private static bool IsAllowedScheme(Uri uri)
        => uri.Scheme is "http" or "https";

    private static bool IsBlockedHost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        foreach (var blocked in BlockedHostPatterns)
        {
            if (host == blocked || host.EndsWith("." + blocked))
                return true;
        }

        if (host.StartsWith("10.") || host.StartsWith("192.168.") || host.StartsWith("172."))
            return true;

        return false;
    }

    private static async Task<bool> ResolvesToPrivateIpAsync(Uri uri)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);
            foreach (var addr in addresses)
            {
                if (IPAddress.IsLoopback(addr))
                    return true;

                var bytes = addr.GetAddressBytes();
                if (addr.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
                {
                    // 10.x.x.x
                    if (bytes[0] == 10) return true;
                    // 172.16-31.x.x
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                    // 192.168.x.x
                    if (bytes[0] == 192 && bytes[1] == 168) return true;
                    // 169.254.x.x (link-local / cloud metadata)
                    if (bytes[0] == 169 && bytes[1] == 254) return true;
                }

                if (addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv6LinkLocal)
                    return true;
            }
        }
        catch
        {
            // DNS resolution failure — block to be safe
            return true;
        }

        return false;
    }
}
