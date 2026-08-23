// Each test boots the WASM runtime in a fresh browser page, which is CPU-heavy; run them one at a time
// so several runtime boots don't contend and time out under load.
[NotInParallel]
public class SnapshotTests
{
    static WebApplication? app;
    static int port;
    static IPlaywright? playwright;
    static IBrowser? browser;

    [Before(Class)]
    public static async Task OneTimeSetUp()
    {
        port = GetAvailablePort();

        // Use pre-published output from build (see csproj PublishBlazorForTests target)
        var testAssemblyDir = Path.GetDirectoryName(typeof(SnapshotTests).Assembly.Location)!;
        var wwwrootPath = Path.Combine(testAssemblyDir, "..", "blazor-publish", "wwwroot");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Logging.ClearProviders();

        app = builder.Build();

        var contentTypeProvider = new FileExtensionContentTypeProvider
        {
            Mappings =
            {
                [".wasm"] = "application/wasm"
            }
        };

        var fileProvider = new PhysicalFileProvider(wwwrootPath);

        app.UseDefaultFiles(
            new DefaultFilesOptions
            {
                FileProvider = fileProvider
            });
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = fileProvider,
                ContentTypeProvider = contentTypeProvider,
                ServeUnknownFileTypes = true
            });

        app.MapFallbackToFile(
            "index.html",
            new StaticFileOptions
            {
                FileProvider = fileProvider
            });

        await app.StartAsync();

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync();
    }

    [After(Class)]
    public static async Task OneTimeTearDown()
    {
        if (browser != null)
        {
            await browser.CloseAsync();
        }

        playwright?.Dispose();

        if (app != null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task HomePage()
    {
        var page = await NewPinnedPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");

        await SettleAsync(page);

        await VerifyPinnedAsync(page);
    }

    // End-to-end on the real WASM runtime: uploading a DOCX reads it, renders each page, and paints a
    // page-image preview. If the parse/render pipeline were broken the preview never appears and this
    // times out.
    [Test]
    public async Task UploadingDocumentRendersPreview()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);

        await UploadSampleAsync(page);

        var image = await page.QuerySelectorAsync(".preview-page");
        var source = await image!.GetAttributeAsync("src");

        await Assert.That(source).StartsWith("data:image/png");
    }

    // One sample per readable format is bundled as a static web asset; clicking its button fetches, reads
    // and renders it off the UI thread — the same pipeline as an upload but sourced from the asset. Run
    // for all three inputs because each routes through a different Morph parser, and the published build
    // is trimmed: a source whose parser lost a type it reflects on fails only here.
    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task SampleRendersPreview(InputFormat source)
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);

        await page.ClickAsync(SampleButton(source));

        var image = await page.WaitForSelectorAsync(
            ".preview-page",
            new()
            {
                Timeout = 90000
            });
        var url = await image!.GetAttributeAsync("src");

        await Assert.That(url).StartsWith("data:image/png");
    }

    // The Download button runs the actual conversion (off the UI thread) and hands the bytes to the
    // browser. Markdown is deterministic and needs no fonts — a clean check the download path works.
    [Test]
    public async Task DownloadingMarkdownSavesFile()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);

        await UploadSampleAsync(page);

        await page.SelectOptionAsync(".convert-panel .format-select", "Markdown");

        var download = await page.RunAndWaitForDownloadAsync(
            () => page.ClickAsync(".convert-btn"),
            new()
            {
                Timeout = 30000
            });

        await Assert.That(download.SuggestedFilename).EndsWith(".md");
    }

    // PDF is rendered from fonts materialised into the WASM in-memory filesystem (PdfSharp can't read
    // Morph's embedded fonts). Exercising it in a real browser validates the whole FontStore fetch → MEMFS
    // write → directory-scan chain — a regression there fails only here, not in the desktop service tests.
    [Test]
    public async Task DownloadingPdfSavesFile()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);

        await UploadSampleAsync(page);

        await page.SelectOptionAsync(".convert-panel .format-select", "Pdf");

        var download = await page.RunAndWaitForDownloadAsync(
            () => page.ClickAsync(".convert-btn"),
            new()
            {
                Timeout = 30000
            });

        await Assert.That(download.SuggestedFilename).EndsWith(".pdf");
    }

    // On a wide viewport (Playwright's default 1280px exceeds the 1200px breakpoint) selecting a non-PNG
    // format converts immediately and shows the output in the result pane: text formats inline, PDF and
    // HTML through a blob-URL iframe. Selecting PNG removes the pane — the page preview already is the PNG.
    [Test]
    public async Task SelectingFormatShowsResultPane()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);

        await UploadSampleAsync(page);

        await page.SelectOptionAsync(".convert-panel .format-select", "Markdown");
        var text = await page.WaitForSelectorAsync(
            ".result-text",
            new()
            {
                Timeout = 30000
            });
        // The sample document embeds images; the pane view swaps their base64 payloads for size notes.
        await Assert.That(await text!.TextContentAsync()).Contains("KB elided");
        // ...and captions that swap under the header.
        var note = await page.WaitForSelectorAsync(
            ".result-note",
            new()
            {
                Timeout = 30000
            });
        await Assert.That(await note!.TextContentAsync()).Contains("omitted for brevity");

        await page.SelectOptionAsync(".convert-panel .format-select", "Html");
        var frame = await page.WaitForSelectorAsync(
            ".result-frame",
            new()
            {
                Timeout = 30000
            });
        var source = await frame!.GetAttributeAsync("src");
        await Assert.That(source).StartsWith("blob:");

        await page.SelectOptionAsync(".convert-panel .format-select", "Png");
        await page.WaitForSelectorAsync(
            ".result-pane",
            new()
            {
                State = WaitForSelectorState.Detached,
                Timeout = 30000
            });
    }

    [Test]
    public async Task HomePageMobile()
    {
        var page = await NewPinnedPageAsync();
        // iPhone SE size
        await page.SetViewportSizeAsync(375, 667);

        await page.GotoAsync($"http://localhost:{port}/");

        await SettleAsync(page);

        await VerifyPinnedAsync(page);
    }

    [Test]
    public async Task HomePageDarkMode()
    {
        var page = await NewPinnedPageAsync();

        await page.GotoAsync($"http://localhost:{port}/");

        // Set dark theme in localStorage before Blazor initializes
        await page.EvaluateAsync("() => localStorage.setItem('selectedTheme', 'Dark')");

        // Reload to apply theme
        await page.ReloadAsync();

        await SettleAsync(page);

        await VerifyPinnedAsync(page);
    }

    [Test]
    public async Task HomePageDarkModeMobile()
    {
        var page = await NewPinnedPageAsync();
        // iPhone SE size
        await page.SetViewportSizeAsync(375, 667);

        await page.GotoAsync($"http://localhost:{port}/");

        // Set dark theme in localStorage before Blazor initializes
        await page.EvaluateAsync("() => localStorage.setItem('selectedTheme', 'Dark')");

        // Reload to apply theme
        await page.ReloadAsync();

        await SettleAsync(page);

        await VerifyPinnedAsync(page);
    }

    // The app's stylesheet deliberately uses a system font stack, which resolves to Segoe UI on the Windows machine a
    // baseline is captured on and to DejaVu on the Linux CI runner. That is a different typeface rather
    // than sub-pixel drift, so the page screenshots' SSIM fell as the page gained text — two mobile
    // snapshots dropped under the 0.7 threshold on CI while their HTML matched byte for byte. Pin every
    // face to the Aptos the app already ships so a screenshot is OS-independent, and the comparison
    // measures real layout and colour regressions instead of spending its tolerance on fonts.
    //
    // Rewriting the stylesheet response rather than injecting a <style> keeps the captured HTML identical
    // to what the app actually serves — the HTML snapshot stays a record of the real page. Emoji still
    // come from the OS (Aptos has none), but they are a negligible share of the frame.
    const string fontPin = """

        @font-face { font-family: 'SnapshotPin'; font-weight: 400; font-style: normal; src: url('/_content/Morph.Blazor/fonts/Aptos_400.ttf') format('truetype'); }
        @font-face { font-family: 'SnapshotPin'; font-weight: 400; font-style: italic; src: url('/_content/Morph.Blazor/fonts/Aptos_400_Italic.ttf') format('truetype'); }
        @font-face { font-family: 'SnapshotPin'; font-weight: 700; font-style: normal; src: url('/_content/Morph.Blazor/fonts/Aptos_700.ttf') format('truetype'); }
        @font-face { font-family: 'SnapshotPin'; font-weight: 700; font-style: italic; src: url('/_content/Morph.Blazor/fonts/Aptos_700_Italic.ttf') format('truetype'); }
        * { font-family: 'SnapshotPin', sans-serif !important; }
        """;

    static async Task<IPage> NewPinnedPageAsync()
    {
        var page = await browser!.NewPageAsync();
        await page.RouteAsync(
            "**/css/app.css",
            async route =>
            {
                var response = await route.FetchAsync();
                var css = await response.TextAsync();
                await route.FulfillAsync(
                    new()
                    {
                        Response = response,
                        Body = css + fontPin,
                        ContentType = "text/css"
                    });
            });
        return page;
    }

    // Guards the pin above: if the route pattern ever stops matching the stylesheet, the screenshots
    // would silently revert to OS fonts and start drifting across machines again — passing locally and
    // failing only on CI, which is exactly the failure this replaced.
    static async Task VerifyPinnedAsync(IPage page)
    {
        var family = await page.EvaluateAsync<string>("() => getComputedStyle(document.body).fontFamily");
        await Assert.That(family).Contains("SnapshotPin");

        await PinFooterAsync(page);

        await Verify(page);
    }

    // The footer carries three figures that vary from one capture to the next: the version comes from
    // AssemblyInformationalVersion (SDK-suffixed with the commit SHA, so it moves every commit), the
    // download total is measured from Resource Timing, and the RAM figure is the live WebAssembly heap.
    // Scrubbing them out of the captured HTML is not enough — they are also painted into the screenshot,
    // where they shift pixels the SSIM comparison would otherwise have to absorb. Pin the text in the DOM
    // before the capture instead, so HTML and PNG agree and neither drifts. Each placeholder keeps the
    // shape of the real string so the footer's layout stays representative.
    static async Task PinFooterAsync(IPage page)
    {
        var pinned = await page.EvaluateAsync<int>(
            """
            () => {
                const values = {
                    '.footer-version': 'v0.0.0+0000000',
                    '.footer-size': '0.0 MB zipped · 0.0 MB unzipped',
                    '.footer-ram': '0.0 MB RAM (0.0 MB peak)'
                };
                let pinned = 0;
                for (const [selector, value] of Object.entries(values)) {
                    const element = document.querySelector(selector);
                    if (element) {
                        element.textContent = value;
                        pinned++;
                    }
                }
                return pinned;
            }
            """);
        // Guards the pin: if a class name changes, the live figures would silently return to the
        // screenshots and start drifting again.
        await Assert.That(pinned).IsEqualTo(3);
    }

    static async Task UploadSampleAsync(IPage page, InputFormat source = InputFormat.Docx)
    {
        var info = ConversionService.Find(source);
        await page.SetInputFilesAsync(
            "#source-file",
            new FilePayload
            {
                Name = info.SampleFileName,
                MimeType = info.ContentType,
                Buffer = Sample.BytesFor(source)
            });
        await page.WaitForSelectorAsync(
            ".preview-page",
            new()
            {
                Timeout = 90000
            });
    }

    // The sample buttons render in ReadableFormats order, so the format's index picks its button.
    static string SampleButton(InputFormat source) =>
        $".sample-btn:nth-of-type({ConversionService.ReadableFormats.ToList().FindIndex(_ => _.Format == source) + 1})";

    // Waits for the app to be fully settled before a snapshot: the upload UI present, every asset loaded,
    // and web fonts rendered — so the captured screenshot is the deterministic settled page rather than a
    // mid-boot frame.
    static async Task SettleAsync(IPage page)
    {
        await page.WaitForSelectorAsync(".file-drop");
        // The cold first boot downloads the whole (untrimmed) framework, so give NetworkIdle generous
        // headroom — the heaviest, run-first test pays the full asset download before the network quiets.
        await page.WaitForLoadStateAsync(
            LoadState.NetworkIdle,
            new()
            {
                Timeout = 120000
            });
        // The theme toggle's label is driven by MainLayout.OnInitializedAsync (an async preference
        // load), so wait for the label to agree with data-theme — otherwise a dark-theme screenshot can
        // catch the pre-flip "Dark" label.
        await page.WaitForFunctionAsync(
            """
            () => {
                const dark = document.documentElement.getAttribute('data-theme') === 'dark';
                const b = document.querySelector('.theme-toggle-btn');
                return b && (dark ? b.textContent.includes('Light') : b.textContent.includes('Dark'));
            }
            """);
        await page.EvaluateAsync("() => document.fonts.ready");
        // The footer's download total and RAM figure are filled in from an async interop call off the first
        // render; wait for them so the captured HTML/PNG always includes them rather than racing a partial
        // footer. Match on Attached, not the default Visible, since the payload size is display:none at the
        // mobile viewport — it's still in the DOM, which is all we need to know the interop has completed.
        await page.WaitForSelectorAsync(
            ".footer-size",
            new()
            {
                State = WaitForSelectorState.Attached
            });
        await page.WaitForSelectorAsync(
            ".footer-ram",
            new()
            {
                State = WaitForSelectorState.Attached
            });
    }

    static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint) listener.LocalEndpoint).Port;
    }
}
