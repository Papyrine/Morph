using System.Text.RegularExpressions;

// What a page costs drawn as SVG (MorphViewer.VectorPages) against painted as a PNG, on the real
// WebAssembly runtime: the viewer opens with ?pages=svg-timed, which writes each page as SVG, paints the
// PNG it replaces unseen, and logs both times; the script records its own part (parsing the markup,
// decoding the pictures) and the frame that first shows the page.
//
// Explicit, like Benchmark_Editor. Build Release and run
//
//   dotnet src/Morph.Web.Tests/bin/Release/net10.0/Morph.Web.Tests.dll --treenode-filter "/*/*/SnapshotTests/Benchmark_VectorPages"
//
// then read benchmarks/vector-*.md beside the test assembly.
public partial class SnapshotTests
{
    [Test]
    [Explicit]
    public async Task Benchmark_VectorPages()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "benchmarks");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"vector-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.md");
        var builder = new StringBuilder();
        builder.AppendLine("# Pages drawn as SVG against painted, in the browser");
        builder.AppendLine();
        builder.AppendLine(
            $"Chromium {browser!.Version}, {Environment.ProcessorCount} logical cores, {DateTime.Now:yyyy-MM-dd HH:mm}. Milliseconds per page, on the " +
            "WebAssembly runtime's one thread: the SVG written by .NET, the PNG painted by .NET at the DPI the viewer asked for, " +
            "the script's part of showing the SVG (parsing it, decoding its pictures), and the frame after it is put in the page.");
        builder.AppendLine();
        builder.AppendLine("| Document | Page | DPI | SVG written | PNG painted | ratio | script | frame | SVG kchars |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        var all = new List<(double Svg, double Png)>();
        foreach (var (name, bytes) in VectorDocuments())
        {
            var page = await browser.NewPageAsync();
            try
            {
                var lines = new List<string>();
                page.Console += (_, message) =>
                {
                    if (message.Text.StartsWith("morph-paint", StringComparison.Ordinal))
                    {
                        lock (lines)
                        {
                            lines.Add(message.Text);
                        }
                    }
                };
                await page.GotoAsync($"http://localhost:{port}/view?pages=svg-timed");
                await page.WaitForSelectorAsync(
                    ".viewer-open input[type=file]",
                    new()
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = 120000
                    });
                await page.SetInputFilesAsync(
                    ".viewer-open input[type=file]",
                    new FilePayload
                    {
                        Name = "timed.docx",
                        MimeType = ConversionService.Find(InputFormat.Docx).ContentType,
                        Buffer = bytes
                    });
                await page.WaitForSelectorAsync(
                    ".viewer-page[data-current][data-rendered-dpi]",
                    new()
                    {
                        Timeout = 600000
                    });
                var count = await page.Locator(".viewer-page").CountAsync();
                for (var index = 1; index <= count; index++)
                {
                    await page.EvaluateAsync($"() => document.querySelector('.viewer-page[data-page-number=\"{index}\"]').scrollIntoView({{ block: 'center' }})");
                    await page.WaitForSelectorAsync(
                        $".viewer-page[data-page-number=\"{index}\"][data-rendered-dpi]",
                        new()
                        {
                            Timeout = 600000
                        });
                }

                await page.WaitForTimeoutAsync(500);
                var script = await page.EvaluateAsync<JsonElement>("() => window.morphVectorTimings ?? []");
                var scripts = script.EnumerateArray()
                    .GroupBy(_ => _.GetProperty("page").GetInt32())
                    .ToDictionary(_ => _.Key, _ => _.First());
                List<string> logged;
                lock (lines)
                {
                    logged = [.. lines];
                }

                foreach (var line in logged.DistinctBy(_ => PageNumber.Match(_).Groups[1].Value))
                {
                    var number = int.Parse(PageNumber.Match(line).Groups[1].Value, CultureInfo.InvariantCulture);
                    var svg = Field(line, "svg");
                    var png = Field(line, "png");
                    all.Add((svg, png));
                    var shown = scripts.GetValueOrDefault(number);
                    var scriptTime = shown.ValueKind == JsonValueKind.Object ? shown.GetProperty("script").GetDouble() : double.NaN;
                    var frame = shown.ValueKind == JsonValueKind.Object ? shown.GetProperty("frame").GetDouble() : double.NaN;
                    builder.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"| {name} | {number} | {Field(line, "dpi"):0} | {svg:0} | {png:0} | {png / Math.Max(svg, 0.1):0}x | {scriptTime:0.0} | {frame:0.0} | {Field(line, "svg-bytes") / 1000:0.0} |");
                }

                Console.WriteLine($"{name}: {logged.Count} pages timed of {count}");
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        if (all.Count > 0)
        {
            var svgs = all.Select(_ => _.Svg).Order().ToList();
            var pngs = all.Select(_ => _.Png).Order().ToList();
            builder.AppendLine();
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"Over {all.Count} pages: SVG written in {svgs[svgs.Count / 2]:0} ms at the median (max {svgs[^1]:0}), PNG painted in {pngs[pngs.Count / 2]:0} ms (max {pngs[^1]:0}); in total {all.Sum(_ => _.Svg):0} against {all.Sum(_ => _.Png):0} ms.");
        }

        await File.WriteAllTextAsync(path, builder.ToString());
        Console.WriteLine(builder.ToString());
        Console.WriteLine($"Written to {path}");
    }

    static double Field(string line, string name) =>
        double.Parse(Regex.Match(line, $"(?:^| ){Regex.Escape(name)}=([0-9.]+)").Groups[1].Value, CultureInfo.InvariantCulture);

    static IEnumerable<(string Name, byte[] Bytes)> VectorDocuments()
    {
        yield return ("The app's sample", Sample.DocxBytes);
        yield return ("60 plain paragraphs", Plain(60));
        yield return ("Business plan", File.ReadAllBytes(ProjectFiles.corpus.business_plan_docx));
        yield return ("Table of contents", File.ReadAllBytes(ProjectFiles.corpus.table_of_contents_docx));
        yield return ("Header and footer", File.ReadAllBytes(ProjectFiles.corpus.header_footer_docx));
        yield return ("Tracked changes", File.ReadAllBytes(ProjectFiles.corpus.tracked_changes_docx));
    }

    [GeneratedRegex(@"page=(\d+)")]
    private static partial Regex PageNumber { get; }
}
