using System.Globalization;
using System.Text.Json;

// End to end on the real WASM runtime: the selectable text layer over the converter's preview, and the
// /view document viewer — its pages, toolbar, find, download and print. These run in a real browser
// because that is where the text layer's geometry, copying and highlighting actually happen.
public partial class SnapshotTests
{
    // Selecting a whole page's layer and copying it yields exactly the text the builder produced — the same
    // characters find searches — with the synthesised spaces, tabs and line breaks in place.
    [Test]
    public async Task PreviewTextLayer_CopiesThePageText()
    {
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/");
        await SettleAsync(page);
        await UploadSampleAsync(page);
        await page.WaitForSelectorAsync(
            ".preview-sheet .text-layer[data-text-state=ready]",
            new()
            {
                Timeout = 90000
            });

        var copied = await page.EvaluateAsync<string?>(copyLayer, ".preview-sheet .text-layer");

        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
        await Assert.That(copied).IsEqualTo(document.TextLayer(0).Text);
    }

    // Trimming failures only surface in the published app, so each format's path runs here.
    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task ViewerRendersSample(InputFormat source)
    {
        var page = await OpenViewerAsync(source);

        using var document = PagedDocument.Open(Sample.BytesFor(source), source, Sample.FontDirectory);
        await Assert.That(await page.GetAttributeAsync(".viewer", "data-page-count")).IsEqualTo(document.PageCount.ToString(CultureInfo.InvariantCulture));
        await Assert.That(await page.GetAttributeAsync(".viewer-page[data-current] img", "src")).StartsWith("blob:");
    }

    [Test]
    public async Task ViewerToolbar_ZoomsNavigatesRotatesAndShowsThumbnails()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);
        var scale = await NumberAsync(page, "() => document.querySelector('.viewer').dataset.scale");
        var dpi = await NumberAsync(page, "() => document.querySelector('.viewer-page[data-current]').dataset.renderedDpi");

        // Zooming in re-renders the page at a resolution to match.
        await page.ClickAsync("button[aria-label='Zoom in']");
        await page.WaitForFunctionAsync("before => Number(document.querySelector('.viewer').dataset.scale) > before", scale);
        await page.WaitForFunctionAsync(
            "before => Number(document.querySelector('.viewer-page[data-current]').dataset.renderedDpi) > before",
            dpi,
            new()
            {
                Timeout = 60000
            });

        await page.ClickAsync("button[aria-label='Next page']");
        await page.WaitForSelectorAsync(".viewer[data-current-page='2']");
        await page.WaitForFunctionAsync("() => document.querySelector('.viewer-page-input').value === '2'");

        // A quarter turn swaps the page box.
        await page.ClickAsync("button[aria-label='Rotate clockwise']");
        await page.WaitForSelectorAsync(".viewer[data-rotation='90']");
        var box = await page.EvaluateAsync<double[]>("() => { const page = document.querySelector('.viewer-page[data-current]'); return [page.offsetWidth, page.offsetHeight]; }");
        await Assert.That(box[0]).IsGreaterThan(box[1]);

        await page.ClickAsync("button[aria-label='Toggle sidebar']");
        await page.WaitForSelectorAsync(".viewer[data-sidebar]");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('.viewer-thumb img')].every(_ => _.naturalWidth > 0)",
            null,
            new()
            {
                Timeout = 60000
            });
        await page.ClickAsync(".viewer-thumb[data-page-index='0']");
        await page.WaitForSelectorAsync(".viewer[data-current-page='1']");
    }

    // The phrase wraps between two lines on the sample's first page; the browser's own find cannot match
    // across that, the viewer's can.
    [Test]
    public async Task ViewerFind_MatchesAPhraseAcrossALineBreak()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);

        await page.ClickAsync("button[aria-label='Find in document']");
        await page.FillAsync(".viewer-find-input", "the new Secretary");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.viewer-find-status')?.textContent === '1 of 2'",
            null,
            new()
            {
                Timeout = 30000
            });

        var current = await page.EvaluateAsync<string?>("() => [...(CSS.highlights.get('morph-find-current') ?? [])][0]?.toString()");
        await Assert.That(current).IsEqualTo("the new Secretary");

        await page.PressAsync(".viewer-find-input", "Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('.viewer-find-status')?.textContent === '2 of 2'");
    }

    [Test]
    public async Task ViewerDownload_SavesTheOriginalFile()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);

        var download = await page.RunAndWaitForDownloadAsync(
            () => page.ClickAsync("button[aria-label=Download]"),
            new()
            {
                Timeout = 30000
            });

        await Assert.That(download.SuggestedFilename).IsEqualTo("sample.docx");
        var saved = await File.ReadAllBytesAsync((await download.PathAsync())!);
        await Assert.That(saved.AsSpan().SequenceEqual(Sample.DocxBytes)).IsTrue();
    }

    // The browser's dialog is stubbed: it records what print CSS would show — every page, at the print
    // resolution — then closes, and the viewer must clean the print pages away.
    [Test]
    public async Task ViewerPrint_LaysOutEveryPage_ThenCleansUp()
    {
        var page = await browser!.NewPageAsync();
        await page.AddInitScriptAsync(
            """
            window.print = () => {
                const container = document.querySelector('.print-container');
                window.printed = JSON.stringify({
                    widths: container ? [...container.querySelectorAll('img')].map(_ => _.naturalWidth) : [],
                    printing: document.documentElement.hasAttribute('data-morph-printing')
                });
                setTimeout(() => window.dispatchEvent(new Event('afterprint')), 50);
            };
            """);
        await OpenViewerAsync(InputFormat.Docx, page);

        await page.ClickAsync("button[aria-label=Print]");
        await page.WaitForFunctionAsync(
            "() => window.printed",
            null,
            new()
            {
                Timeout = 60000
            });

        using var printed = JsonDocument.Parse(await page.EvaluateAsync<string>("() => window.printed"));
        var widths = printed.RootElement.GetProperty("widths").EnumerateArray().Select(_ => _.GetInt32()).ToList();
        // Both US Letter pages, at 150 DPI.
        await Assert.That(widths.SequenceEqual([1275, 1275])).IsTrue();
        await Assert.That(printed.RootElement.GetProperty("printing").GetBoolean()).IsTrue();

        await page.WaitForSelectorAsync(
            ".print-container",
            new()
            {
                State = WaitForSelectorState.Detached
            });
        await Assert.That(await page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-morph-printing')")).IsFalse();
    }

    // The layer is sized in container units, so every line must stay on its drawn position at any zoom:
    // its left edge exactly, its right edge within the browser's own advances of Aptos. Unpinned page:
    // the snapshot font pin would override the layer's face.
    [Test]
    public async Task ViewerTextLayer_LinesSitOnTheirDrawnPositions_AtEveryZoom()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);
        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
        var expected = TopLevelLines(document.TextLayer(0).Json);

        foreach (var zoom in new[] { 1d, 2d })
        {
            await page.SelectOptionAsync(".viewer-zoom", zoom.ToString(CultureInfo.InvariantCulture));
            await page.WaitForFunctionAsync("zoom => Math.abs(Number(document.querySelector('.viewer').dataset.scale) - zoom) < 0.001", zoom);

            using var measured = JsonDocument.Parse(await page.EvaluateAsync<string>(measureLines));
            var lines = measured.RootElement.EnumerateArray().Select(_ => (Left: _[0].GetDouble(), Right: _[1].GetDouble())).ToList();

            await Assert.That(lines.Count).IsEqualTo(expected.Count);
            for (var index = 0; index < lines.Count; index++)
            {
                var (left, right) = expected[index];
                await Assert.That(Math.Abs(lines[index].Left - left)).IsLessThan(0.75);
                await Assert.That(Math.Abs(lines[index].Right - right)).IsLessThan(Math.Max(2, (right - left) * 0.02));
            }
        }
    }

    // Opens /view (in page, if given) and loads the format's bundled sample, waiting until the first page
    // has painted and has its text layer.
    static async Task<IPage> OpenViewerAsync(InputFormat source, IPage? page = null)
    {
        page ??= await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/view");
        var index = ConversionService.ReadableFormats.ToList().FindIndex(_ => _.Format == source) + 1;
        await page.ClickAsync(
            $".viewer-empty .sample-btn:nth-of-type({index})",
            new()
            {
                Timeout = 120000
            });
        await page.WaitForSelectorAsync(
            ".viewer-page[data-current][data-rendered-dpi] .text-layer[data-text-state=ready]",
            new()
            {
                Timeout = 90000
            });
        return page;
    }

    static async Task<double> NumberAsync(IPage page, string expression) =>
        double.Parse(await page.EvaluateAsync<string>(expression), CultureInfo.InvariantCulture);

    // The expected horizontal extent, in points, of each top-level line that has text.
    static List<(double Left, double Right)> TopLevelLines(string json)
    {
        using var layer = JsonDocument.Parse(json);
        var lines = new List<(double Left, double Right)>();
        foreach (var item in layer.RootElement.GetProperty("i").EnumerateArray())
        {
            if (item.TryGetProperty("k", out _) ||
                item.GetProperty("s").GetArrayLength() == 0)
            {
                continue;
            }

            var spans = item.GetProperty("s");
            var last = spans[spans.GetArrayLength() - 1];
            lines.Add((spans[0].GetProperty("x").GetDouble(), last.GetProperty("x").GetDouble() + last.GetProperty("w").GetDouble()));
        }

        return lines;
    }

    // Selects a text layer's whole content and dispatches a copy, returning what the layer's copy handler
    // put on the (synthetic) clipboard — no clipboard permission needed.
    const string copyLayer =
        """
        selector => {
            const layer = document.querySelector(selector);
            const range = document.createRange();
            range.selectNodeContents(layer);
            const selection = getSelection();
            selection.removeAllRanges();
            selection.addRange(range);
            const data = new DataTransfer();
            const event = new ClipboardEvent('copy', { clipboardData: data, bubbles: true, cancelable: true });
            layer.dispatchEvent(event);
            return event.defaultPrevented ? data.getData('text/plain') : null;
        }
        """;

    // Each top-level text line of page 1 as [left, right] in points: the drawn text only, without the
    // wrap or tab marker a copy adds after it.
    const string measureLines =
        """
        () => {
            const page = document.querySelector('.viewer-page[data-page-number="1"]');
            const sheet = page.querySelector('.viewer-sheet');
            const box = sheet.getBoundingClientRect();
            const scale = box.width / Number(getComputedStyle(page).getPropertyValue('--pw'));
            const lines = [...sheet.querySelectorAll('.text-line:not(.text-empty):not(.text-box)')]
                .filter(_ => !_.closest('.text-frame'))
                .map(line => {
                    const range = document.createRange();
                    range.setStart(line, 0);
                    const last = line.lastElementChild;
                    if (last && last === line.lastChild && (last.classList.contains('text-wrap') || last.classList.contains('text-tab'))) {
                        range.setEndBefore(last);
                    } else {
                        range.setEnd(line, line.childNodes.length);
                    }

                    const rects = [...range.getClientRects()].filter(_ => _.width > 0);
                    const left = Math.min(...rects.map(_ => _.left));
                    const right = Math.max(...rects.map(_ => _.right));
                    return [(left - box.left) / scale, (right - box.left) / scale];
                });
            return JSON.stringify(lines);
        }
        """;
}
