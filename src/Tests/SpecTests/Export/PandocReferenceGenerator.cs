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

        foreach (var directory in directories)
        {
            var input = Path.Combine(directory, "input.docx");
            await TryConvert(() => PandocInstance.Convert<DocxIn, HtmlOut>(input, Path.Combine(directory, "expected.html")));
            await TryConvert(() => PandocInstance.Convert<DocxIn, CommonMarkOut>(input, Path.Combine(directory, "expected.md")));
            await TryConvert(() => PandocInstance.Convert<DocxIn, PdfOut>(input, Path.Combine(directory, "expected.pdf")));
        }
    }

    static async Task TryConvert(Func<Task> convert)
    {
        try
        {
            await convert();
        }
        catch
        {
            // A format (typically PDF, which needs a LaTeX engine) may be unavailable on this
            // machine — skip it rather than aborting the whole seeding pass.
        }
    }
}
