using Core.Contracts;
using System.ComponentModel;

namespace IAW.Agents.Web;

public interface IPlaywright : IAgent
{
    static string IAgent.AgentDisplayName => "Playwright";

    static string IAgent.AgentDescription =>
        "Automates browser interactions — navigates pages, scrapes content, extracts structured data using Playwright MCP tools.";

    static string[] IAgent.AgentCapabilities =>
        ["browser", "scrape", "web", "navigate", "extract", "screenshot", "automation"];

    static string[] IAgent.AgentRoutingExamples =>
        ["open this website", "scrape page content", "take a screenshot of the page",
         "navigate to URL", "extract data from webpage", "browse to"];

    static string IAgent.AgentInstructions => """
        You are Playwright, the browser automation specialist. You navigate web pages, scrape content,
        and extract structured data using browser tools.

        RULES:
        - ALWAYS call browser_navigate first to load a page before any other browser action.
        - Use browser_snapshot to read page content via the accessibility tree — this is your primary way to "see" the page.
        - Use browser_evaluate to run JavaScript for precise data extraction when the accessibility tree is insufficient.
        - Use browser_click and browser_fill to interact with dynamic pages (pagination, forms, dropdowns).
        - Return extracted data in a clear, structured format (lists, tables, key-value pairs).
        - DO NOT attempt to navigate to login-protected pages without explicit credentials.
        - DO NOT scrape pages faster than one request per 2 seconds to be respectful.
        - If a page fails to load or times out, report the error clearly — do not retry silently.
        """;

    [Description("Navigate to a URL, read the page using browser tools, and extract data according to the given instructions. Returns the extracted content as structured text.")]
    Task<string> ScrapePageAsync(string url, string instructions, CancellationToken ct = default);

    [Description("Navigate to a URL and evaluate a JavaScript expression to extract data directly. Returns the JS evaluation result as a string.")]
    Task<string> ExtractDataAsync(string url, string jsExpression, CancellationToken ct = default);
}
