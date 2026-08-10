using Morph.PDFium;

/// <summary>
/// Scenario tests for the HTML, Markdown and PDF exporters, following the same per-input-directory
/// Verify pattern as <c>SkiaScenarioTests</c> and enumerating every scenario across every input
/// format. Our output is snapshotted as <c>html_result.verified.html</c>,
/// <c>md_result.verified.md</c> and <c>pdf_result.verified.pdf</c> beside each input.
///
/// Each OUTPUT format is its own test so a diff in one doesn't block the others (important when bulk
/// re-seeding baselines), and each uses a distinct file-name stem so the formats don't race over
/// orphan-cleanup of each other's received/verified files. INPUT formats stay in one class instead —
/// they share every stem, so splitting them would reintroduce exactly that race.
/// </summary>
public class ExportScenarioTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    public static IEnumerable<string> Scenarios() => ScenarioInputs.AllDirectories();

    // The exporters are format-blind below ParsedDocument, so the only per-format part is which
    // parser produces it.
    static string ToHtml(string input, HtmlExportOptions? options = null) =>
        ScenarioInputs.FormatOf(Path.GetDirectoryName(input)!) == ScenarioFormat.PowerPoint
            ? PowerPointConverter.ConvertToHtml(input, options)
            : DocumentConverter.ConvertToHtml(input, options);

    static string ToMarkdown(string input, MarkdownExportOptions? options = null) =>
        ScenarioInputs.FormatOf(Path.GetDirectoryName(input)!) == ScenarioFormat.PowerPoint
            ? PowerPointConverter.ConvertToMarkdown(input, options)
            : DocumentConverter.ConvertToMarkdown(input, options);

    static byte[] ToPdf(string input, PdfExportOptions options) =>
        ScenarioInputs.FormatOf(Path.GetDirectoryName(input)!) == ScenarioFormat.PowerPoint
            ? PdfPowerPointConverter.ConvertToPdf(input, options)
            : PdfDocumentConverter.ConvertToPdf(input, options);

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task HtmlOutput(string directory)
    {
        ContainerOnly.Require();
        var html = ToHtml(ScenarioInputs.InputFile(directory));
        var png = await BrowserScreenshot.RenderHtmlAsync(html);
        Target[] targets =
        [
            new("html", html),
            new("png", new MemoryStream(png))
        ];
        await Verify(targets)
            .UseDirectory(directory)
            .UseFileName("html_result")
            .IgnoreParameters();
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task MarkdownOutput(string directory)
    {
        ContainerOnly.Require();
        var markdown = ToMarkdown(ScenarioInputs.InputFile(directory));
        var png = await BrowserScreenshot.RenderMarkdownAsync(markdown);
        var targets = new[]
        {
            new Target("md", markdown),
            new Target("png", new MemoryStream(png))
        };
        await Verify(targets)
            .UseDirectory(directory)
            .UseFileName("md_result")
            .IgnoreParameters();
    }

    // Render PDF pages at the same DPI VerifyPDFium uses (see VerifyPDFium.Initialize in
    // ModuleInitializer) so the pixels measured here match the pdf_result#page_*.verified.png
    // snapshots and the 150-DPI Word reference PNGs (expected_*.png) the metric compares against.
    const double pdfRenderDpi = 150;

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task PdfOutput(string directory)
    {
        ContainerOnly.Require();
        var input = ScenarioInputs.InputFile(directory);
        var pdf = ToPdf(
            input,
            new()
            {
                FontDirectory = fontsDirectory,
                Pages = ScenarioInputs.Pages(ScenarioInputs.FormatOf(directory))
            });

        // Record a per-page ErrorMetric against the Word reference (expected_*.png), mirroring the
        // Skia/ImageSharp scenario tests, so pdf_result.verified.json carries the same PageDiffs and
        // compare-all-pdf.md can show how close each PDF page is to Word.
        var expectedFiles = Directory.GetFiles(directory, "expected_*.png")
            .Order()
            .ToArray();
        var diffs = PdfPageDiffs(pdf, expectedFiles);

        // PdfRenderer already pins every source of per-save variance (MakeDeterministic for the
        // dates and trailer /ID, Normalize for the font subset tags and XMP uuids), so letting
        // Verify.PDFium neutralize them again copies the whole buffer, rescans it, and rebuilds it
        // to canonicalize XMP whitespace — work that changes nothing here. The snapshot therefore
        // holds Morph's own bytes, which is also what makes the .verified.pdf worth reading.
        var settings = Verify(pdf, extension: "pdf")
            .SkipPdfNormalization()
            .UseDirectory(directory)
            .UseFileName("pdf_result")
            .IgnoreParameters();
        if (diffs != null)
        {
            settings = settings.AppendValue("PageDiffs", diffs);
        }

        await settings;
    }

    // Mirrors ImageSharpScenarioTests.PageDiffs, but the pages come from rasterising the produced
    // PDF with the same PDFium engine VerifyPDFium uses. Null when the page count doesn't match the
    // Word reference (PDF pagination can differ), in which case no metric is recorded.
    static List<PageDiff>? PdfPageDiffs(byte[] pdf, string[] expectedFiles)
    {
        using var document = PdfiumDocument.Load(pdf);
        if (expectedFiles.Length != document.PageCount)
        {
            return null;
        }

        var diffs = new List<PageDiff>(document.PageCount);
        for (var page = 0; page < document.PageCount; page++)
        {
            var expectedFile = expectedFiles[page];
            var rendered = document.RenderPage(page, pdfRenderDpi);

            var (errorMetric, ssim) = PageComparison.Compare(expectedFile, rendered);
            diffs.Add(new(page + 1, errorMetric, ssim, Path.GetFileName(expectedFile), $"pdf_result#page_{page + 1:0000}.verified.png", $"pdf_result#page_{page + 1:0000}.received.png"));
        }

        return diffs;
    }
}
