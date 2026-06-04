/// <summary>
/// Scenario tests for the HTML, Markdown and PDF exporters, following the same per-input-directory
/// Verify pattern as <c>SkiaScenarioTests</c> and enumerating every <c>input.docx</c> under
/// <c>Inputs/</c>. Our output is snapshotted as <c>html_result.verified.html</c>,
/// <c>md_result.verified.md</c> and <c>pdf_result.verified.pdf</c> beside each input; Pandoc's
/// output (seeded by <see cref="PandocReferenceGenerator"/>) sits alongside as <c>expected.html</c> /
/// <c>expected.md</c> / <c>expected.pdf</c> for visual comparison.
///
/// Each format is its own test so a diff in one doesn't block the others (important when bulk
/// re-seeding baselines), and each uses a distinct file-name stem so the formats don't race over
/// orphan-cleanup of each other's received/verified files.
/// </summary>
public class ExportScenarioTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    public static IEnumerable<string> Scenarios()
    {
        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        return Directory.GetFiles(inputs, "input.docx", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)!;
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task HtmlOutput(string directory)
    {
        var html = DocumentConverter.ConvertToHtml(Path.Combine(directory, "input.docx"));
        var png = await BrowserScreenshot.RenderHtmlAsync(html);
        Target[] targets = [
            new("html", html),
            new("png", new MemoryStream(png))
        ];
        await Verify(
            targets)
            .UseDirectory(directory)
            .UseFileName("html_result")
            .IgnoreParameters();
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task MarkdownOutput(string directory)
    {
        var markdown = DocumentConverter.ConvertToMarkdown(Path.Combine(directory, "input.docx"));
        var png = await BrowserScreenshot.RenderMarkdownAsync(markdown);
        var targets = new[]
        {
            new Target("md", markdown),
            new Target("png", new MemoryStream(png))
        };
        await Verify(
                targets)
            .UseDirectory(directory)
            .UseFileName("md_result")
            .IgnoreParameters();
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task PdfOutput(string directory)
    {
        var input = Path.Combine(directory, "input.docx");
        var pdf = PdfDocumentConverter.ConvertToPdf(input, new()
        {
            FontDirectory = fontsDirectory
        });

        // Snapshotted as raw bytes — not via Verify, whose ImageMagick plugin would rasterize a
        // "pdf" target to PNG (pulling in a Ghostscript dependency). PdfRenderer makes the bytes
        // reproducible (pinned dates/ID, normalized font-subset tags) so a straight byte compare
        // against the committed pdf_result.verified.pdf is stable. The "pdf_result" stem keeps it
        // out of the html/md verifications' cleanup paths.
        var snapshot = Path.Combine(directory, "pdf_result.verified.pdf");
        var received = Path.Combine(directory, "pdf_result.received.pdf");

        if (File.Exists(snapshot) && File.ReadAllBytes(snapshot).AsSpan().SequenceEqual(pdf))
        {
            File.Delete(received);
            return;
        }

        await File.WriteAllBytesAsync(received, pdf);
        throw new($"PDF output differs from pdf_result.verified.pdf in {directory}. " +
                  "Review pdf_result.received.pdf and, if correct, rename it over pdf_result.verified.pdf.");
    }
}
