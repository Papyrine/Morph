using Markdig;
using Microsoft.Playwright;

/// <summary>
/// Shared Playwright (Chromium) instance for rendering HTML and Markdown fragments to PNG. The
/// browser is launched lazily on first use and reused across all tests in the run; each call gets
/// its own page so concurrent screenshots don't collide.
/// Rendering is pinned to the repo's bundled fonts (<c>src/Fonts/</c>) via injected
/// <c>@font-face</c> rules so the screenshots are reproducible across machines. Without this,
/// Chromium falls back to whatever fonts the host OS has installed, so text reflows between the
/// local box and CI (different metrics → different line wrapping → different full-page height) and
/// the pixel-diff comparison drifts. Loading <c>file://</c> font URLs requires the page to have a
/// <c>file://</c> origin (hence the temp-file navigation rather than <c>SetContent</c>'s opaque
/// <c>about:blank</c>) and the <c>--allow-file-access-from-files</c> launch arg.
/// </summary>
static class BrowserScreenshot
{
    static readonly SemaphoreSlim launchLock = new(1, 1);
    static IPlaywright? playwright;
    static IBrowser? browser;

    /// <summary>
    /// Timeout for every browser operation that acts on the rendered page — navigation and
    /// screenshot alike.
    ///
    /// Playwright's default of 30s is too short for the PowerPoint decks: a deck's Markdown export
    /// embeds every slide's artwork inline as base64, so the page is a single strip tens of megabytes
    /// tall. Loading that page is the same weight as capturing it, so it applies to both.
    ///
    /// Generous on purpose: an unused timeout costs nothing.
    /// </summary>
    const int pageTimeout = 180_000;

    /// <summary>
    /// What Chromium answers when it has no frame to hand back for a capture. It is not a timeout
    /// and it is not about the page: it strikes tiny pages rarely and unreproducibly, and captures in
    /// separate contexts together, so the capture is made once more on a new page, and
    /// <see cref="Report"/> says so. The second PNG goes through Verify like any other,
    /// so a retry cannot pass a rendering change.
    /// </summary>
    const string captureFailure = "Unable to capture screenshot";

    static readonly TextWriter diagnostics = TextWriter.Synchronized(
        new StreamWriter(Console.OpenStandardError())
        {
            AutoFlush = true
        });

    static string gpuProcessAtLaunch = "unknown";

    static readonly string fontsDirectory =
        Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    // Built once on first use: an @font-face rule per bundled face (keyed by every family-name
    // candidate, with its real weight/italic), pointing at the file on disk. Chromium only decodes
    // the faces a page actually uses, so the unused rules are cheap.
    static readonly Lazy<string> fontFaceCss = new(BuildFontFaceCss);

    static readonly MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Renders an HTML fragment to a PNG of what it draws. The page is laid out in a 1024 CSS pixel
    /// window, or in one as wide as its body's max-width when that is wider, and the capture is cut
    /// to the content (see <see cref="CaptureAsync"/>).
    ///
    /// <paramref name="deviceScale"/> is device pixels per CSS pixel. Below 1 it shrinks the capture
    /// without touching layout — the viewport stays 1024 CSS pixels wide, so nothing reflows and the
    /// screenshot stays reproducible; only the sampling density drops. That distinction is what makes
    /// it safe to vary per scenario format, where narrowing the viewport would not be.
    /// </summary>
    public static async Task<byte[]> RenderHtmlAsync(string html, double deviceScale = 1)
    {
        // Render from a real file:// page so the file:// @font-face URLs are same-scheme and load
        // (SetContent runs on an opaque about:blank origin, which blocks local font files).
        var tempFile = Path.Combine(Path.GetTempPath(), $"morph-screenshot-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempFile, WrapHtmlFragment(html));
        try
        {
            try
            {
                return await CaptureAsync(tempFile, deviceScale);
            }
            catch (PlaywrightException exception)
                when (exception.Message.Contains(captureFailure))
            {
                Report($"{captureFailure}. {await DescribeBrowserAsync()} Capturing again on a new page.");
            }

            return await CaptureAsync(tempFile, deviceScale);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    static async Task<byte[]> CaptureAsync(string file, double deviceScale)
    {
        var instance = await GetBrowserAsync();
        await using var context = await instance.NewContextAsync(
            new()
            {
                ViewportSize = new()
                {
                    Width = viewportWidth,
                    Height = viewportHeight
                },
                DeviceScaleFactor = (float) deviceScale
            });
        var page = await context.NewPageAsync();
        page.Crash += (_, _) => Report("A page's renderer process crashed.");

        await page.GotoAsync(
            new Uri(file).AbsoluteUri,
            new()
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = pageTimeout
            });
        // Block until every @font-face the page references has finished loading, so no glyph is
        // captured mid-swap from a fallback face. EvaluateAsync takes no timeout and is not
        // bound by the default one, so this await is not a candidate for the same failure.
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");

        // A page wider than the window (a deck's 960pt slides are 1280px) is laid out in a window
        // its width: in 1024px its body is squeezed to the window and every slide, which clips
        // to its box, loses its right fifth.
        var pageWidth = await page.EvaluateAsync<double>(pageWidthScript);
        if (pageWidth > viewportWidth)
        {
            await page.SetViewportSizeAsync((int) Math.Ceiling(pageWidth), viewportHeight);
        }

        // The capture is what the page draws, not the window: a full-page capture grows the window
        // down to the content but never trims it, which would leave cards/04's 672x480 card in a
        // 1024x768 frame.
        var extent = await page.EvaluateAsync<double[]>(contentExtentScript);
        return await page.ScreenshotAsync(
            new()
            {
                FullPage = true,
                Clip = new()
                {
                    X = 0,
                    Y = 0,
                    Width = (float) Math.Max(1, Math.Ceiling(extent[0])),
                    Height = (float) Math.Max(1, Math.Ceiling(extent[1]))
                },
                Type = ScreenshotType.Png,
                Timeout = pageTimeout
            });
    }

    const int viewportWidth = 1024;
    const int viewportHeight = 768;

    // The width the body asks for: its max-width plus its padding and borders (the page width the
    // HTML export writes there), or 0 when it sets no max-width.
    const string pageWidthScript =
        """
        () => {
          const style = getComputedStyle(document.body);
          const max = parseFloat(style.maxWidth);
          if (!isFinite(max)) {
            return 0;
          }
          return max +
            parseFloat(style.paddingLeft) + parseFloat(style.paddingRight) +
            parseFloat(style.borderLeftWidth) + parseFloat(style.borderRightWidth);
        }
        """;

    // The right and bottom edges of everything the page draws, in page coordinates: every element's
    // box, cut to the boxes of the ancestors that clip their overflow, so art a slide hides past its
    // edge does not widen the capture.
    const string contentExtentScript =
        """
        () => {
          let right = 0;
          let bottom = 0;
          const visit = (element, clip) => {
            const style = getComputedStyle(element);
            if (style.display === 'none') {
              return;
            }
            const box = element.getBoundingClientRect();
            const shown = {
              left: Math.max(box.left, clip.left),
              top: Math.max(box.top, clip.top),
              right: Math.min(box.right, clip.right),
              bottom: Math.min(box.bottom, clip.bottom)
            };
            if (shown.right > shown.left && shown.bottom > shown.top) {
              right = Math.max(right, shown.right);
              bottom = Math.max(bottom, shown.bottom);
            }
            const inner = style.overflowX === 'visible' && style.overflowY === 'visible' ? clip : shown;
            for (const child of element.children) {
              visit(child, inner);
            }
          };
          visit(document.body, {left: -Infinity, top: -Infinity, right: Infinity, bottom: Infinity});
          return [right + window.scrollX, bottom + window.scrollY];
        }
        """;

    /// <summary>
    /// The state of the browser at a failed capture: whether it is still there, how many captures
    /// were in flight, and its GPU process against the one it launched with. Never throws, since
    /// it runs where a failure is already being handled.
    /// </summary>
    static async Task<string> DescribeBrowserAsync()
    {
        if (browser is not {IsConnected: true} instance)
        {
            return "Chromium is no longer connected.";
        }

        var inFlight = instance.Contexts.Count;
        var gpuProcess = await GpuProcessAsync(instance);
        return $"{inFlight} other captures in flight, GPU process {gpuProcess} (launched with {gpuProcessAtLaunch}).";
    }

    // The pid and CPU seconds of Chromium's GPU process. Every page is composited there, so it is
    // the one process a capture depends on that outlives the page.
    static async Task<string> GpuProcessAsync(IBrowser instance)
    {
        try
        {
            var session = await instance.NewBrowserCDPSessionAsync();
            var info = await session.SendAsync("SystemInfo.getProcessInfo");
            await session.DetachAsync();
            foreach (var process in info!.Value.GetProperty("processInfo").EnumerateArray())
            {
                if (process.GetProperty("type").GetString() == "GPU")
                {
                    return $"{process.GetProperty("id").GetInt32()} after {process.GetProperty("cpuTime").GetDouble():F1}s of CPU";
                }
            }

            return "absent";
        }
        catch (Exception exception)
        {
            return $"unknown ({exception.Message})";
        }
    }

    // TUnit keeps what a test writes to Console and shows it only if the test fails, so a retry
    // that rescued its test would leave no trace. The process's own stderr reaches the log.
    static void Report(string message) =>
        diagnostics.WriteLine($"BrowserScreenshot: {message}");

    public static Task<byte[]> RenderMarkdownAsync(string markdown, double deviceScale = 1) =>
        RenderHtmlAsync(Markdown.ToHtml(markdown, markdownPipeline), deviceScale);

    static string WrapHtmlFragment(string html)
    {
        var fontStyle = $"<style>{fontFaceCss.Value}</style>";

        // If the input already looks like a full document, splice the bundled-font rules into its
        // head (after <head>, else after <html>, else at the front) so they take effect there too.
        var trimmed = html.TrimStart();
        if (trimmed.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
            if (headIndex >= 0)
            {
                var afterHead = html.IndexOf('>', headIndex) + 1;
                return html.Insert(afterHead, fontStyle);
            }

            var htmlIndex = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (htmlIndex >= 0)
            {
                var afterHtml = html.IndexOf('>', htmlIndex) + 1;
                return html.Insert(afterHtml, fontStyle);
            }

            return fontStyle + html;
        }

        // Otherwise embed the fragment in a minimal document with a neutral body so the rendered PNG
        // isn't influenced by browser chrome / default margins.
        return $"<!doctype html><html><head><meta charset=\"utf-8\">{fontStyle}</head><body>{html}</body></html>";
    }

    static string BuildFontFaceCss()
    {
        var builder = new StringBuilder();
        var fontFiles = Directory.EnumerateFiles(fontsDirectory)
            .Where(IsFontFile)
            .OrderBy(_ => _, StringComparer.OrdinalIgnoreCase);

        foreach (var path in fontFiles)
        {
            var url = new Uri(path).AbsoluteUri;
            foreach (var (face, names) in OpenTypeReader.ReadFaces(path))
            {
                var fontStyle = face.Italic ? "italic" : "normal";
                var weight = face.Weight is >= 1 and <= 1000 ? face.Weight : 400;
                foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append("@font-face{font-family:\"");
                    builder.Append(EscapeFamily(name));
                    builder.Append("\";font-weight:");
                    builder.Append(weight);
                    builder.Append(";font-style:");
                    builder.Append(fontStyle);
                    builder.Append(";src:url(\"");
                    builder.Append(url);
                    builder.Append("\");}");
                }
            }
        }

        return builder.ToString();
    }

    static bool IsFontFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
    }

    static string EscapeFamily(string name) =>
        name.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
            browser = await playwright.Chromium.LaunchAsync(
                new()
                {
                    Headless = true,
                    Args =
                    [
                        // Let the file:// page load the file:// @font-face URLs.
                        "--allow-file-access-from-files",
                        // Strip platform-specific text rasterization so two machines match: no hinting,
                        // no LCD subpixel AA, fixed sRGB profile.
                        "--font-render-hinting=none",
                        "--disable-lcd-text",
                        "--force-color-profile=srgb"
                    ]
                });
            browser.Disconnected += (_, _) => Report("Chromium disconnected.");
            gpuProcessAtLaunch = await GpuProcessAsync(browser);
        }
        finally
        {
            launchLock.Release();
        }

        return browser;
    }
}
