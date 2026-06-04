using Microsoft.Playwright;
using Markdig;

/// <summary>
/// Shared Playwright (Chromium) instance for rendering HTML and Markdown fragments to PNG. The
/// browser is launched lazily on first use and reused across all tests in the run; each call gets
/// its own page so concurrent screenshots don't collide.
/// </summary>
static class BrowserScreenshot
{
    static readonly SemaphoreSlim launchLock = new(1, 1);
    static IPlaywright? playwright;
    static IBrowser? browser;

    static readonly MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static async Task<byte[]> RenderHtmlAsync(string html)
    {
        var instance = await GetBrowserAsync();
        await using var context = await instance.NewContextAsync(new()
        {
            ViewportSize = new() {Width = 1024, Height = 768}
        });
        var page = await context.NewPageAsync();
        await page.SetContentAsync(WrapHtmlFragment(html), new() {WaitUntil = WaitUntilState.Load});
        return await page.ScreenshotAsync(new() {FullPage = true, Type = ScreenshotType.Png});
    }

    public static Task<byte[]> RenderMarkdownAsync(string markdown) =>
        RenderHtmlAsync(Markdown.ToHtml(markdown, markdownPipeline));

    static string WrapHtmlFragment(string html)
    {
        // If the input already looks like a full document, hand it through; otherwise embed it in
        // a minimal document with a neutral body so the rendered PNG isn't influenced by browser
        // chrome / default margins.
        var trimmed = html.TrimStart();
        if (trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        return $"<!doctype html><html><head><meta charset=\"utf-8\"></head><body>{html}</body></html>";
    }

    static async Task<IBrowser> GetBrowserAsync()
    {
        if (browser is {IsConnected: true})
        {
            return browser;
        }

        await launchLock.WaitAsync();
        try
        {
            if (browser is {IsConnected: true})
            {
                return browser;
            }

            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new() {Headless = true});
        }
        finally
        {
            launchLock.Release();
        }

        return browser;
    }
}
