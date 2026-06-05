using Pandoc;

/// <summary>
/// Dev-only utility that seeds Pandoc "ground truth" reference files (<c>expected.html</c>,
/// <c>expected.md</c>, <c>expected.pdf</c>) beside each scenario <c>input.docx</c>, using PandocNet
/// (a wrapper over the installed <c>pandoc</c> CLI). These sit next to the Verify-snapshotted
/// <c>results_*</c> files the way Word's <c>expected_*.png</c> sits beside the raster snapshots.
///
/// Skipped during normal runs. Enable by setting the <c>MORPH_GEN_PANDOC_REFS</c> environment
/// variable to <c>curated</c> (the <see cref="ExportScenarioTests"/> subset) or <c>all</c> (every
/// input). Pandoc must be installed; PDF generation additionally needs a LaTeX engine and is
/// best-effort.
/// </summary>
public class PandocReferenceGenerator
{
    [Test]
    public async Task GenerateReferences()
    {
        var mode = Environment.GetEnvironmentVariable("MORPH_GEN_PANDOC_REFS");
        if (string.IsNullOrEmpty(mode))
        {
            return;
        }

        var directories = mode.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetFiles(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs"), "input.docx", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(_ => _ != null)
                .Select(_ => _!)
            : ExportScenarioTests.Scenarios();

        // Pandoc shells out to an external engine for PDF. Point at one or more via
        // MORPH_PANDOC_PDF_ENGINE (comma-separated exe names on PATH or full paths, e.g.
        // "xelatex,typst"). Each is tried in order until one yields a non-empty PDF — no single
        // engine handles every document (typst stumbles on extracted images, Pandoc's LaTeX table
        // writer stumbles on merged cells). Unset → Pandoc's default (pdflatex).
        var pdfEngines = (Environment.GetEnvironmentVariable("MORPH_PANDOC_PDF_ENGINE") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in directories)
        {
            var input = Path.Combine(directory, "input.docx");
            await TryConvert(Path.Combine(directory, "expected.html"), output => PandocInstance.Convert<DocxIn, HtmlOut>(input, output));
            await TryConvert(Path.Combine(directory, "expected.md"), output => PandocInstance.Convert<DocxIn, CommonMarkOut>(input, output));
            await ConvertPdf(Path.Combine(directory, "expected.pdf"), input, pdfEngines);
        }
    }

    static async Task ConvertPdf(string output, string input, string[] engines)
    {
        if (engines.Length == 0)
        {
            await TryConvert(output, target => PandocInstance.Convert<DocxIn, PdfOut>(input, target));
            return;
        }

        foreach (var engine in engines)
        {
            await TryConvert(output, target => PandocInstance.Convert<DocxIn, PdfOut>(input, target, outOptions: new() {EnginePath = engine}));
            if (File.Exists(output))
            {
                return;
            }
        }
    }

    static async Task TryConvert(string output, Func<string, Task> convert)
    {
        try
        {
            await convert(output);
        }
        catch
        {
            // A format (notably PDF, which needs a LaTeX engine Pandoc shells out to) may be
            // unavailable on this machine — fall through to the empty-file cleanup below.
        }

        // Pandoc opens the output before invoking its engine, so a failed conversion leaves a
        // 0-byte file behind. Drop it rather than committing a misleading empty reference.
        if (File.Exists(output) && new FileInfo(output).Length == 0)
        {
            File.Delete(output);
        }
    }
}
