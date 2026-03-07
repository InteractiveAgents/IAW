using System.ComponentModel;

namespace Core.V3.Tools;

public class WebTools(HttpClient httpClient)
{
    [Description("Fetch content from a URL")]
    public async Task<string> FetchUrlAsync([Description("URL to fetch")] string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return content.Length > 50_000 ? content[..50_000] + "\n... (truncated)" : content;
        }
        catch (Exception ex)
        {
            return $"Error fetching {url}: {ex.Message}";
        }
    }
}
