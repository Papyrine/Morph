using static EditFixtures;

// Pages drawn as SVG (MorphViewer.VectorPages, the default), end to end in the published app.
public partial class SnapshotTests
{
    // An SVG is drawn at whatever size the page is shown: a zoom keeps it rather than asking .NET again.
    [Test]
    public async Task VectorPages_ZoomKeepsTheDrawnPage()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);
        await page.EvaluateAsync("() => window.drawnBefore = document.querySelector('.viewer-page[data-current] svg.viewer-vector')");
        var scale = await NumberAsync(page, "() => document.querySelector('.viewer').dataset.scale");

        await page.ClickAsync("button[aria-label='Zoom in']");
        await page.WaitForFunctionAsync("before => Number(document.querySelector('.viewer').dataset.scale) > before", scale);
        await page.WaitForTimeoutAsync(1000);

        await Assert.That(await page.EvaluateAsync<bool>("() => document.querySelector('.viewer-page[data-current] svg.viewer-vector') === window.drawnBefore")).IsTrue();
        await Assert.That(await page.GetAttributeAsync(".viewer-page[data-current]", "data-rendered-dpi")).IsEqualTo("vector");
    }

    [Test]
    public async Task VectorPages_ThumbnailsAreDrawnAsSvg()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);
        await page.ClickAsync("button[aria-label='Toggle sidebar']");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('.viewer-thumb')].every(_ => _.querySelector('svg.viewer-thumb-vector text'))",
            null,
            new()
            {
                Timeout = 60000
            });
    }

    // The browser's own find has to find a page's words in the text layer only. A page's SVG and its
    // thumbnail's each stand alone in an inert HTML holder: on the svg itself `inert` does nothing, and
    // Chromium's find bar (probed through UI Automation, which a test cannot drive) then counted every word
    // three times; held, its counts are the painted pages' own.
    [Test]
    public async Task VectorPages_AreInertToTheBrowsersFind()
    {
        var page = await OpenViewerAsync(InputFormat.Docx);
        await page.ClickAsync("button[aria-label='Toggle sidebar']");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('.viewer-thumb')].every(_ => _.querySelector('svg.viewer-thumb-vector'))",
            null,
            new()
            {
                Timeout = 60000
            });
        var held = await page.EvaluateAsync<bool[]>(
            """
            () => [...document.querySelectorAll('svg.viewer-vector, svg.viewer-thumb-vector')]
                .map(_ => _.parentElement instanceof HTMLElement && _.parentElement.inert && _.parentElement.childElementCount === 1)
            """);
        await Assert.That(held.Length).IsGreaterThan(2);
        await Assert.That(held).DoesNotContain(false);
    }

    // Each printed page is the SVG, at the page's size in points, with its text as text.
    [Test]
    public async Task VectorPages_PrintsEveryPageAsSvg()
    {
        var page = await browser!.NewPageAsync();
        await page.AddInitScriptAsync(printStub);
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
        var vectors = printed.RootElement.GetProperty("vectors").EnumerateArray().Select(_ => _.GetString()).ToList();
        // Both US Letter pages.
        await Assert.That(vectors.SequenceEqual(["0 0 612 792", "0 0 612 792"])).IsTrue();
        await Assert.That(printed.RootElement.GetProperty("widths").GetArrayLength()).IsEqualTo(0);
        await Assert.That(printed.RootElement.GetProperty("texts").GetInt32()).IsGreaterThan(0);

        await page.WaitForSelectorAsync(
            ".print-container",
            new()
            {
                State = WaitForSelectorState.Detached
            });
    }

    // A long document read to its end keeps no more drawn pages than painted ones: the far pages go back
    // to their thumbnails, and come back drawn when they come near again.
    [Test]
    public async Task VectorPages_FarPagesAreLetGo()
    {
        var filler = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 11));
        var docx = Build(string.Concat(Enumerable.Range(1, 240).Select(_ => P("", R($"Paragraph {_}. {filler}")))));
        var page = await browser!.NewPageAsync();
        await page.GotoAsync($"http://localhost:{port}/view");
        await LoadIntoAsync(page, docx, "long.docx");
        var count = await page.Locator(".viewer-page").CountAsync();
        await Assert.That(count).IsGreaterThan(32);

        for (var number = 1; number <= count; number++)
        {
            await page.EvaluateAsync($"() => document.querySelector('.viewer-page[data-page-number=\"{number}\"]').scrollIntoView({{ block: 'center' }})");
            await page.WaitForSelectorAsync(
                $".viewer-page[data-page-number=\"{number}\"] svg.viewer-vector",
                new()
                {
                    Timeout = 60000
                });
        }

        await Assert.That(await page.Locator("svg.viewer-vector").CountAsync()).IsLessThanOrEqualTo(24);
        await Assert.That(await page.Locator(".viewer-page[data-page-number=\"1\"] svg.viewer-vector").CountAsync()).IsEqualTo(0);

        await page.EvaluateAsync("() => document.querySelector('.viewer-page[data-page-number=\"1\"]').scrollIntoView()");
        await page.WaitForSelectorAsync(
            ".viewer-page[data-page-number=\"1\"] svg.viewer-vector",
            new()
            {
                Timeout = 60000
            });
    }
}
