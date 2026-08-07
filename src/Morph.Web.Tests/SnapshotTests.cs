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
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");

        await SettleAsync(page);

        await Verify(page);
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

        await page.SetInputFilesAsync("#docx-file", new FilePayload
        {
            Name = "sample.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Buffer = Sample.DocxBytes
        });

        var image = await page.WaitForSelectorAsync(
            ".preview-page",
            new()
            {
                Timeout = 90000
            });
        var source = await image!.GetAttributeAsync("src");

        await Assert.That(source).StartsWith("data:image/png");
    }

    // The sample document is bundled as a static web asset; clicking "Try a sample document" fetches,
    // reads and renders it off the UI thread — the same pipeline as an upload but sourced from the asset.
    [Test]
    public async Task SampleDocumentRendersPreview()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);

        await page.ClickAsync(".sample-btn");

        var image = await page.WaitForSelectorAsync(
            ".preview-page",
            new()
            {
                Timeout = 90000
            });
        var source = await image!.GetAttributeAsync("src");

        await Assert.That(source).StartsWith("data:image/png");
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
        var text = await page.WaitForSelectorAsync(".result-text", new()
        {
            Timeout = 30000
        });
        // The sample document embeds images; the pane view swaps their base64 payloads for size notes.
        await Assert.That(await text!.TextContentAsync()).Contains("KB elided");
        // ...and captions that swap under the header.
        var note = await page.WaitForSelectorAsync(".result-note", new()
        {
            Timeout = 30000
        });
        await Assert.That(await note!.TextContentAsync()).Contains("omitted for brevity");

        await page.SelectOptionAsync(".convert-panel .format-select", "Html");
        var frame = await page.WaitForSelectorAsync(".result-frame", new()
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
        var page = await browser!.NewPageAsync();
        // iPhone SE size
        await page.SetViewportSizeAsync(375, 667);

        await page.GotoAsync($"http://localhost:{port}/");

        await SettleAsync(page);

        await Verify(page);
    }

    [Test]
    public async Task HomePageDarkMode()
    {
        var page = await browser!.NewPageAsync();

        await page.GotoAsync($"http://localhost:{port}/");

        // Set dark theme in localStorage before Blazor initializes
        await page.EvaluateAsync("() => localStorage.setItem('selectedTheme', 'Dark')");

        // Reload to apply theme
        await page.ReloadAsync();

        await SettleAsync(page);

        await Verify(page);
    }

    [Test]
    public async Task HomePageDarkModeMobile()
    {
        var page = await browser!.NewPageAsync();
        // iPhone SE size
        await page.SetViewportSizeAsync(375, 667);

        await page.GotoAsync($"http://localhost:{port}/");

        // Set dark theme in localStorage before Blazor initializes
        await page.EvaluateAsync("() => localStorage.setItem('selectedTheme', 'Dark')");

        // Reload to apply theme
        await page.ReloadAsync();

        await SettleAsync(page);

        await Verify(page);
    }

    static async Task UploadSampleAsync(IPage page)
    {
        await page.SetInputFilesAsync("#docx-file", new FilePayload
        {
            Name = "sample.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Buffer = Sample.DocxBytes
        });
        await page.WaitForSelectorAsync(".preview-page", new()
        {
            Timeout = 90000
        });
    }

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
        await page.WaitForSelectorAsync(".footer-size", new()
        {
            State = WaitForSelectorState.Attached
        });
        await page.WaitForSelectorAsync(".footer-ram", new()
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
