/// <summary>
/// Scenario tests for the HTML, Markdown and PDF exporters, following the same per-input-directory
/// Verify pattern as <c>SkiaScenarioTests</c>. Our output is snapshotted as <c>results.verified.html</c>,
/// <c>results.verified.md</c> and <c>results.verified.pdf</c> beside each <c>input.docx</c>; Pandoc's
/// output (seeded by <see cref="PandocReferenceGenerator"/>) sits alongside as <c>expected.html</c> /
/// <c>expected.md</c> / <c>expected.pdf</c> for visual comparison.
///
/// A curated, representative subset of the ~320 inputs is used so the committed baselines stay
/// reviewable; <see cref="PandocReferenceGenerator"/> can seed the full corpus on demand.
/// </summary>
public class ExportScenarioTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static readonly string[] curated =
    [
        "bold_text",
        "align_center",
        "hyperlinks",
        "bullet_list",
        "numbered_list",
        "nested_list",
        "simple_table",
        "complex_tables",
        "headings",
        "inline_image"
    ];

    public static IEnumerable<string> Scenarios()
    {
        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        foreach (var name in curated)
        {
            var directory = Path.Combine(inputs, name);
            if (File.Exists(Path.Combine(directory, "input.docx")))
            {
                yield return directory;
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task Scenario(string directory)
    {
        var input = Path.Combine(directory, "input.docx");

        // The PDF is snapshotted as raw bytes (not via Verify, whose ImageMagick plugin would
        // rasterize the "pdf" extension to a PNG and pull in a Ghostscript dependency). PdfRenderer
        // makes the bytes reproducible — pinned dates/ID and normalized font-subset tags — so a
        // straight byte compare against the committed results.verified.pdf is stable.
        var pdf = PdfDocumentConverter.ConvertToPdf(input, new() {FontDirectory = fontsDirectory});
        await VerifyPdfBytes(directory, pdf);

        var html = DocumentConverter.ConvertToHtml(input);
        var markdown = DocumentConverter.ConvertToMarkdown(input);

        // One verifier owns both text outputs so they share the "results" stem without the two
        // formats fighting over orphan cleanup: results.verified.html and results.verified.md.
        await Verify(
                new[]
                {
                    new Target("html", html),
                    new Target("md", markdown)
                })
            .UseDirectory(directory)
            .UseFileName("results")
            .IgnoreParameters();
    }

    // Snapshotted as a plain results.pdf (no Verify ".verified." infix) — the same way Pandoc's
    // expected.pdf sits in the directory untouched by Verify, which would otherwise rasterize a
    // "pdf" target to PNG (via its ImageMagick plugin) and flag a "results.verified.pdf" as a
    // dangling file of the html/md verification's "results" prefix.
    static async Task VerifyPdfBytes(string directory, byte[] pdf)
    {
        var snapshot = Path.Combine(directory, "results.pdf");
        var received = Path.Combine(directory, "results.received.pdf");

        if (File.Exists(snapshot) && File.ReadAllBytes(snapshot).AsSpan().SequenceEqual(pdf))
        {
            File.Delete(received);
            return;
        }

        await File.WriteAllBytesAsync(received, pdf);
        throw new($"PDF output differs from results.pdf in {directory}. " +
                  "Review results.received.pdf and, if correct, rename it over results.pdf.");
    }
}
