/// <summary>
/// Scenario tests for the HTML, Markdown and PDF exporters, following the same per-input-directory
/// Verify pattern as <c>SkiaScenarioTests</c> and enumerating every <c>input.docx</c> under
/// <c>Inputs/</c>. Our output is snapshotted as <c>results.verified.html</c>, <c>results.verified.md</c>
/// and <c>morph.pdf</c> beside each input; Pandoc's output (seeded by
/// <see cref="PandocReferenceGenerator"/>) sits alongside as <c>expected.html</c> / <c>expected.md</c> /
/// <c>expected.pdf</c> for visual comparison.
///
/// Text and PDF are separate tests so a diff in one doesn't block the other (important when bulk
/// re-seeding baselines).
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
    public Task TextOutput(string directory)
    {
        var input = Path.Combine(directory, "input.docx");
        var html = DocumentConverter.ConvertToHtml(input);
        var markdown = DocumentConverter.ConvertToMarkdown(input);

        // One verifier owns both text outputs so they share the "results" stem without the two
        // formats fighting over orphan cleanup: results.verified.html and results.verified.md.
        return Verify(
                new[]
                {
                    new Target("html", html),
                    new Target("md", markdown)
                })
            .UseDirectory(directory)
            .UseFileName("results")
            .IgnoreParameters();
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task PdfOutput(string directory)
    {
        var input = Path.Combine(directory, "input.docx");
        var pdf = PdfDocumentConverter.ConvertToPdf(input, new() {FontDirectory = fontsDirectory});

        // Snapshotted as raw bytes under the "morph" stem — not via Verify, whose ImageMagick plugin
        // would rasterize a "pdf" target to PNG (pulling in a Ghostscript dependency), and not under
        // the "results" stem (whose received-file cleanup in the parallel TextOutput run would race
        // to delete it). PdfRenderer makes the bytes reproducible (pinned dates/ID, normalized
        // font-subset tags) so a straight byte compare against the committed morph.pdf is stable.
        var snapshot = Path.Combine(directory, "morph.pdf");
        var received = Path.Combine(directory, "morph.received.pdf");

        if (File.Exists(snapshot) && File.ReadAllBytes(snapshot).AsSpan().SequenceEqual(pdf))
        {
            File.Delete(received);
            return;
        }

        await File.WriteAllBytesAsync(received, pdf);
        throw new($"PDF output differs from morph.pdf in {directory}. " +
                  "Review morph.received.pdf and, if correct, rename it over morph.pdf.");
    }
}
