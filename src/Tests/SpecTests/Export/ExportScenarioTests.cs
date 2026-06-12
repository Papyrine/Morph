/// <summary>
/// Scenario tests for the HTML, Markdown and PDF exporters, following the same per-input-directory
/// Verify pattern as <c>SkiaScenarioTests</c> and enumerating every <c>input.docx</c> under
/// <c>Inputs/</c>. Our output is snapshotted as <c>html_result.verified.html</c>,
/// <c>md_result.verified.md</c> and <c>pdf_result.verified.pdf</c> beside each input; Pandoc's
/// output (seeded by <see cref="PandocReferenceGenerator"/>) sits alongside as <c>expected.html</c> /
/// <c>expected.pdf</c> for visual comparison.
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
        ContainerOnly.Require();
        var html = DocumentConverter.ConvertToHtml(Path.Combine(directory, "input.docx"));
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
        var markdown = DocumentConverter.ConvertToMarkdown(Path.Combine(directory, "input.docx"));
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

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task PdfOutput(string directory)
    {
        ContainerOnly.Require();
        var input = Path.Combine(directory, "input.docx");
        var pdf = PdfDocumentConverter.ConvertToPdf(
            input,
            new()
            {
                FontDirectory = fontsDirectory
            });

        await Verify(pdf, extension: "pdf")
            .UseDirectory(directory)
            .UseFileName("pdf_result")
            .IgnoreParameters();
    }
}
