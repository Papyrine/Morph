// How close a page drawn as SVG (MorphViewer.VectorPages) comes to the PNG ImageSharpPainter paints of
// it, and what each costs. Every sampled page is painted as a PNG and written as SVG through
// PagedDocument, as the viewer does, with the viewer's fonts (every family on the bundled Aptos); the SVG
// is drawn by Chromium at the PNG's own size; and the two are compared ink for ink.
//
// Explicit, so it never runs in the suite. Build Release and run
//
//   dotnet src/Morph.Web.Tests/bin/Release/net10.0/Morph.Web.Tests.dll --treenode-filter "/*/*/VectorPageComparison/*"
//
// then read svg-compare/report.md beside the test assembly, with an overlay per page beside it: black
// where both drew ink, red where only the PNG did, blue where only the SVG did. MORPH_SVG_EVERY (default
// 3) takes every nth Word scenario, MORPH_SVG_PAGES (default 2) the first pages of each, MORPH_SVG_DPI
// (default 144) the resolution. Every page of the whole Word corpus is also written as SVG, unseen, to
// count the pages the SVG cannot carry whole.
public class VectorPageComparison
{
    // Ink is anything darker than this luminance; agreement is counted only where the page around a pixel
    // is light (a pixel this bright within lightRadius), so a dark fill does not agree with itself.
    const double inkLuminance = 160;
    const double lightLuminance = 200;
    const int lightRadius = 4;

    static readonly JsonSerializerOptions indented = new()
    {
        WriteIndented = true
    };

    const string shell =
        """
        <!doctype html>
        <html><head><meta charset="utf-8"><style>
        @font-face { font-family: 'Morph Text'; src: url(fonts/Aptos_400.ttf); font-weight: 400; font-style: normal; }
        @font-face { font-family: 'Morph Text'; src: url(fonts/Aptos_400_Italic.ttf); font-weight: 400; font-style: italic; }
        @font-face { font-family: 'Morph Text'; src: url(fonts/Aptos_700.ttf); font-weight: 700; font-style: normal; }
        @font-face { font-family: 'Morph Text'; src: url(fonts/Aptos_700_Italic.ttf); font-weight: 700; font-style: italic; }
        html, body { margin: 0; background: #ffffff; }
        svg { display: block; }
        </style></head><body></body></html>
        """;

    const string draw =
        """
        async ({ markup, width, height, images }) => {
            document.body.innerHTML = markup;
            const svg = document.body.firstElementChild;
            svg.style.width = width + 'px';
            svg.style.height = height + 'px';
            for (const image of svg.querySelectorAll('image[data-i]')) {
                image.setAttribute('href', images[Number(image.dataset.i)]);
            }

            await document.fonts.ready;
            await Promise.all(images.map(_ => { const probe = new Image(); probe.src = _; return probe.decode().catch(() => {}); }));
            const start = performance.now();
            await new Promise(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)));
            return performance.now() - start;
        }
        """;

    [Test]
    [Explicit]
    public async Task Compare()
    {
        var every = Setting("MORPH_SVG_EVERY", 3);
        var pagesEach = Setting("MORPH_SVG_PAGES", 2);
        var dpi = Setting("MORPH_SVG_DPI", 144);
        var output = Path.Combine(AppContext.BaseDirectory, "svg-compare");
        Directory.CreateDirectory(output);
        foreach (var file in Directory.GetFiles(output))
        {
            File.Delete(file);
        }
        var fonts = Path.Combine(AppContext.BaseDirectory, "fonts");
        var corpus = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "Tests", "Inputs", "word"));
        var scenarios = Directory.GetFiles(corpus, "input.docx", SearchOption.AllDirectories)
            .Where(_ => !_.Contains("_probe_"))
            .Order(StringComparer.Ordinal)
            .ToList();

        var coverage = Coverage(scenarios, fonts, dpi);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await browser.NewPageAsync(
            new()
            {
                DeviceScaleFactor = 1
            });
        await page.RouteAsync(
            "http://morph.test/**",
            async route =>
            {
                var path = new Uri(route.Request.Url).AbsolutePath;
                if (path.StartsWith("/fonts/"))
                {
                    await route.FulfillAsync(
                        new()
                        {
                            BodyBytes = await File.ReadAllBytesAsync(Path.Combine(fonts, Path.GetFileName(path))),
                            ContentType = "font/ttf"
                        });
                    return;
                }

                await route.FulfillAsync(
                    new()
                    {
                        Body = shell,
                        ContentType = "text/html"
                    });
            });
        await page.GotoAsync("http://morph.test/");
        await page.EvaluateAsync("() => Promise.all(['normal 400', 'italic 400', 'normal 700', 'italic 700'].map(_ => document.fonts.load(`${_} 16px 'Morph Text'`)))");

        // The first document pays for the JIT; it is drawn and forgotten.
        await CompareDocumentAsync(page, scenarios.First(_ => _.Contains("align_justified")), corpus, fonts, dpi, 1, null);

        var results = new List<PageResult>();
        foreach (var scenario in scenarios.Where((_, index) => index % every == 0))
        {
            try
            {
                await CompareDocumentAsync(page, scenario, corpus, fonts, dpi, pagesEach, results, output);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("not found"))
            {
                Console.WriteLine($"{Name(scenario, corpus)}: neither painter can draw it ({exception.Message.Split('\n')[0]})");
                continue;
            }

            Console.WriteLine($"{Name(scenario, corpus)}: {results.Count} pages");
        }

        var report = Report(results, coverage, dpi, every, pagesEach);
        await File.WriteAllTextAsync(Path.Combine(output, "report.md"), report);
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, indented));
        Console.WriteLine(report);
        Console.WriteLine($"Written to {output}");
    }

    static async Task CompareDocumentAsync(IPage page, string scenario, string corpus, string fonts, int dpi, int pagesEach, List<PageResult>? results, string? output = null)
    {
        using var document = PagedDocument.Open(await File.ReadAllBytesAsync(scenario), InputFormat.Docx, fonts);
        // The render context is made on the first call at a DPI; neither painter is charged for it.
        document.RenderSvg(0, dpi);
        var name = Name(scenario, corpus);
        for (var index = 0; index < Math.Min(pagesEach, document.PageCount); index++)
        {
            SvgPainter.ForgetGlyphs();
            var watch = Stopwatch.StartNew();
            var svg = document.RenderSvg(index, dpi);
            var cold = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            document.RenderSvg(index, dpi);
            var warm = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var png = document.RenderPage(index, dpi);
            var paint = watch.Elapsed.TotalMilliseconds;

            using var painted = Image.Load<Rgba32>(png);
            var width = document.WidthPoints(index) * dpi / 72.0;
            var height = document.HeightPoints(index) * dpi / 72.0;
            await page.SetViewportSizeAsync((int) Math.Ceiling(width), (int) Math.Ceiling(height));
            var images = svg.Images.Select(_ => $"data:image;base64,{Convert.ToBase64String(_)}").ToArray();
            var raster = await page.EvaluateAsync<double>(
                draw,
                new
                {
                    markup = svg.Markup,
                    width,
                    height,
                    images
                });
            var shot = await page.ScreenshotAsync(
                new()
                {
                    Clip = new()
                    {
                        X = 0,
                        Y = 0,
                        Width = painted.Width,
                        Height = painted.Height
                    }
                });
            using var drawn = Image.Load<Rgba32>(shot);

            if (results == null)
            {
                continue;
            }

            var label = $"{name.Replace('/', '_')}_p{index + 1}";
            var comparison = Measure(painted, drawn, output == null ? null : Path.Combine(output, $"{label}.overlay.png"));
            if (output != null)
            {
                await File.WriteAllBytesAsync(Path.Combine(output, $"{label}.png.png"), png);
                await File.WriteAllBytesAsync(Path.Combine(output, $"{label}.svg.png"), shot);
                await File.WriteAllTextAsync(Path.Combine(output, $"{label}.svg"), svg.Markup);
            }

            results.Add(
                new(
                    name,
                    index + 1,
                    svg.Complete,
                    string.Join(", ", svg.Unsupported),
                    svg.Markup.Length,
                    svg.Images.Sum(_ => _.Length),
                    cold,
                    warm,
                    paint,
                    raster,
                    comparison.PngInk,
                    comparison.SvgInk,
                    comparison.Exact,
                    comparison.WithinOne,
                    comparison.ShiftX,
                    comparison.ShiftY,
                    comparison.ShiftedExact));
        }
    }

    // Every page of every scenario written as SVG: how many the SVG carries whole, and why not the rest.
    static CorpusCoverage Coverage(List<string> scenarios, string fonts, int dpi)
    {
        var pages = 0;
        var complete = 0;
        var documentsComplete = 0;
        var reasons = new Dictionary<string, int>();
        var failed = new List<string>();
        foreach (var scenario in scenarios)
        {
            using var document = PagedDocument.Open(File.ReadAllBytes(scenario), InputFormat.Docx, fonts);
            var whole = true;
            var counted = 0;
            var completed = 0;
            try
            {
                for (var index = 0; index < document.PageCount; index++)
                {
                    var svg = document.RenderSvg(index, dpi);
                    counted++;
                    if (svg.Complete)
                    {
                        completed++;
                        continue;
                    }

                    whole = false;
                    foreach (var reason in svg.Unsupported)
                    {
                        reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                    }
                }
            }
            catch (Exception exception)
            {
                // A font the viewer's fallback cannot place stops the PNG painter just the same.
                failed.Add($"{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(scenario)))}/{Path.GetFileName(Path.GetDirectoryName(scenario))}: {exception.Message.Split('\n')[0]}");
                continue;
            }

            pages += counted;
            complete += completed;
            if (whole)
            {
                documentsComplete++;
            }
        }

        return new(scenarios.Count, documentsComplete, pages, complete, reasons, failed);
    }

    static Comparison Measure(Image<Rgba32> painted, Image<Rgba32> drawn, string? overlayPath)
    {
        var width = Math.Min(painted.Width, drawn.Width);
        var height = Math.Min(painted.Height, drawn.Height);
        var pngInk = Ink(painted, width, height);
        var svgInk = Ink(drawn, width, height);
        var light = Light(painted, width, height);

        var pngNear = Dilate(pngInk, width, height);
        var svgNear = Dilate(svgInk, width, height);
        long pngCount = 0, svgCount = 0, pngMatched = 0, svgMatched = 0, pngExact = 0, svgExact = 0;
        for (var index = 0; index < pngInk.Length; index++)
        {
            if (!light[index])
            {
                continue;
            }

            if (pngInk[index])
            {
                pngCount++;
                pngMatched += svgNear[index] ? 1 : 0;
                pngExact += svgInk[index] ? 1 : 0;
            }

            if (svgInk[index])
            {
                svgCount++;
                svgMatched += pngNear[index] ? 1 : 0;
                svgExact += pngInk[index] ? 1 : 0;
            }
        }

        // The shift of the SVG that lines its ink up best with the PNG's: a systematic offset shows here.
        var (bestX, bestY, bestOverlap) = (0, 0, -1L);
        for (var shiftY = -2; shiftY <= 2; shiftY++)
        {
            for (var shiftX = -2; shiftX <= 2; shiftX++)
            {
                long overlap = 0;
                for (var y = Math.Max(0, -shiftY); y < Math.Min(height, height - shiftY); y++)
                {
                    var row = y * width;
                    var shiftedRow = (y + shiftY) * width;
                    for (var x = Math.Max(0, -shiftX); x < Math.Min(width, width - shiftX); x++)
                    {
                        if (light[row + x] && pngInk[row + x] && svgInk[shiftedRow + x + shiftX])
                        {
                            overlap++;
                        }
                    }
                }

                if (overlap > bestOverlap || (overlap == bestOverlap && Math.Abs(shiftX) + Math.Abs(shiftY) < Math.Abs(bestX) + Math.Abs(bestY)))
                {
                    (bestX, bestY, bestOverlap) = (shiftX, shiftY, overlap);
                }
            }
        }

        if (overlayPath != null)
        {
            using var overlay = new Image<Rgba32>(width, height);
            overlay.ProcessPixelRows(
                rows =>
                {
                    for (var y = 0; y < height; y++)
                    {
                        var row = rows.GetRowSpan(y);
                        for (var x = 0; x < width; x++)
                        {
                            var index = y * width + x;
                            row[x] = (pngInk[index], svgInk[index]) switch
                            {
                                (true, true) => new(0, 0, 0),
                                (true, false) => new(230, 0, 0),
                                (false, true) => new(0, 90, 255),
                                _ => light[index] ? new Rgba32(255, 255, 255) : new Rgba32(225, 225, 225)
                            };
                        }
                    }
                });
            overlay.SaveAsPng(overlayPath);
        }

        static double Ratio(long matched, long total)
        {
            if (total == 0)
            {
                return 1;
            }

            return (double) matched / total;
        }

        return new(
            pngCount,
            svgCount,
            Ratio(pngExact + svgExact, pngCount + svgCount),
            Ratio(pngMatched + svgMatched, pngCount + svgCount),
            bestX,
            bestY,
            Ratio(bestOverlap, pngCount));
    }

    static bool[] Ink(Image<Rgba32> image, int width, int height)
    {
        var ink = new bool[width * height];
        image.ProcessPixelRows(
            rows =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        ink[y * width + x] = Luminance(row[x]) < inkLuminance;
                    }
                }
            });
        return ink;
    }

    // Whether some pixel within lightRadius of each one is light: a separable maximum of the luminance.
    static bool[] Light(Image<Rgba32> image, int width, int height)
    {
        var bright = new bool[width * height];
        image.ProcessPixelRows(
            rows =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        bright[y * width + x] = Luminance(row[x]) > lightLuminance;
                    }
                }
            });
        return Spread(bright, width, height, lightRadius);
    }

    static bool[] Dilate(bool[] mask, int width, int height) =>
        Spread(mask, width, height, 1);

    static bool[] Spread(bool[] mask, int width, int height, int radius)
    {
        var across = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var column = x + offset;
                    if (column >= 0 && column < width && mask[y * width + column])
                    {
                        across[y * width + x] = true;
                        break;
                    }
                }
            }
        }

        var spread = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var row = y + offset;
                    if (row >= 0 && row < height && across[row * width + x])
                    {
                        spread[y * width + x] = true;
                        break;
                    }
                }
            }
        }

        return spread;
    }

    static double Luminance(Rgba32 pixel) =>
        0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;

    static string Report(List<PageResult> results, CorpusCoverage coverage, int dpi, int every, int pagesEach)
    {
        var invariant = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        var shown = results.Where(_ => _.Complete).ToList();
        builder.AppendLine("# SVG pages against painted PNGs");
        builder.AppendLine();
        builder.AppendLine(invariant, $"{dpi} DPI, every {every}th Word scenario, its first {pagesEach} pages: {results.Count} pages, of which {shown.Count} the SVG carries whole. Host .NET (JIT), so the times are ratios, not the browser's. Ink is luminance under {inkLuminance}, counted where the page within {lightRadius}px is lighter than {lightLuminance}.");
        builder.AppendLine();
        builder.AppendLine("## Coverage, the whole Word corpus");
        builder.AppendLine();
        builder.AppendLine(invariant, $"{coverage.CompletePages} of {coverage.Pages} pages ({100.0 * coverage.CompletePages / coverage.Pages:0.0}%) and {coverage.CompleteDocuments} of {coverage.Documents} documents are drawn whole as SVG. The rest fall back to the PNG, for:");
        builder.AppendLine();
        foreach (var (reason, count) in coverage.Reasons.OrderByDescending(_ => _.Value))
        {
            builder.AppendLine(invariant, $"- {reason}: {count} pages");
        }

        builder.AppendLine();
        builder.AppendLine(invariant, $"{coverage.Failed.Count} documents are left out: with the viewer's fonts neither painter can draw them (the font-fallback bug fixed on another branch).");

        builder.AppendLine();
        builder.AppendLine("## Agreement, pages drawn whole");
        builder.AppendLine();
        if (shown.Count > 0)
        {
            var withinOne = shown.Select(_ => _.WithinOne).Order().ToList();
            var exact = shown.Select(_ => _.Exact).Order().ToList();
            var totalInk = shown.Sum(_ => _.PngInk + _.SvgInk);
            builder.AppendLine("| | ±1 px | exact |");
            builder.AppendLine("| --- | ---: | ---: |");
            builder.AppendLine(invariant, $"| all ink pooled | {shown.Sum(_ => _.WithinOne * (_.PngInk + _.SvgInk)) / totalInk:P2} | {shown.Sum(_ => _.Exact * (_.PngInk + _.SvgInk)) / totalInk:P2} |");
            builder.AppendLine(invariant, $"| median page | {Percentile(withinOne, 0.5):P2} | {Percentile(exact, 0.5):P2} |");
            builder.AppendLine(invariant, $"| 10th percentile page | {Percentile(withinOne, 0.1):P2} | {Percentile(exact, 0.1):P2} |");
            builder.AppendLine(invariant, $"| worst page | {withinOne[0]:P2} | {exact[0]:P2} |");
            builder.AppendLine();
            var shifts = shown.GroupBy(_ => (_.ShiftX, _.ShiftY)).OrderByDescending(_ => _.Count());
            builder.AppendLine("Best-fitting shift of the SVG onto the PNG, pages: " + string.Join(", ", shifts.Select(_ => $"({_.Key.ShiftX}, {_.Key.ShiftY}) ×{_.Count()}")));
            builder.AppendLine();
            builder.AppendLine("## Cost per page, host JIT, ms");
            builder.AppendLine();
            builder.AppendLine("| | median | p90 | max |");
            builder.AppendLine("| --- | ---: | ---: | ---: |");
            Row("SVG written, glyph cache cold", shown.Select(_ => _.SvgCold));
            Row("SVG written, glyph cache warm (an edit's unchanged lines)", shown.Select(_ => _.SvgWarm));
            Row("PNG painted (ImageSharpPainter + PNG encode)", shown.Select(_ => _.PngPaint));
            Row("Chromium: SVG parsed and first frame", shown.Select(_ => _.Raster));
            Row("SVG size, thousand characters", shown.Select(_ => _.SvgCharacters / 1000.0));
            builder.AppendLine();
        }

        builder.AppendLine("## Pages");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Page | Whole | ±1 px | exact | shift | SVG cold | SVG warm | PNG | Chromium | SVG kchars | not drawn |");
        builder.AppendLine("| --- | ---: | --- | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var result in results.OrderBy(_ => _.WithinOne))
        {
            builder.AppendLine(invariant, $"| {result.Scenario} | {result.Page} | {(result.Complete ? "yes" : "no")} | {result.WithinOne:P2} | {result.Exact:P2} | ({result.ShiftX}, {result.ShiftY}) | {result.SvgCold:0.0} | {result.SvgWarm:0.0} | {result.PngPaint:0.0} | {result.Raster:0.0} | {result.SvgCharacters / 1000.0:0.0} | {result.Unsupported} |");
        }

        return builder.ToString();

        void Row(string label, IEnumerable<double> values)
        {
            var sorted = values.Order().ToList();
            builder.AppendLine(invariant, $"| {label} | {Percentile(sorted, 0.5):0.0} | {Percentile(sorted, 0.9):0.0} | {sorted[^1]:0.0} |");
        }
    }

    static double Percentile(List<double> sorted, double fraction) =>
        sorted[(int) Math.Min(sorted.Count - 1, Math.Floor(fraction * sorted.Count))];

    static string Name(string scenario, string corpus) =>
        Path.GetRelativePath(corpus, Path.GetDirectoryName(scenario)!).Replace('\\', '/');

    static int Setting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    static string SourceDirectory([CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;

    record PageResult(
        string Scenario,
        int Page,
        bool Complete,
        string Unsupported,
        int SvgCharacters,
        int PictureBytes,
        double SvgCold,
        double SvgWarm,
        double PngPaint,
        double Raster,
        long PngInk,
        long SvgInk,
        double Exact,
        double WithinOne,
        int ShiftX,
        int ShiftY,
        double ShiftedExact);

    record Comparison(long PngInk, long SvgInk, double Exact, double WithinOne, int ShiftX, int ShiftY, double ShiftedExact);

    record CorpusCoverage(int Documents, int CompleteDocuments, int Pages, int CompletePages, Dictionary<string, int> Reasons, List<string> Failed);
}
