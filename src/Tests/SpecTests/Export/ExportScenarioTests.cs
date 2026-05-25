/// <summary>
/// Scenario tests for the HTML and Markdown exporters, following the same per-input-directory
/// Verify pattern as <c>SkiaScenarioTests</c>. Our output is snapshotted as <c>results_html</c> /
/// <c>results_markdown</c> beside each <c>input.docx</c>; Pandoc's output (seeded by
/// <see cref="PandocReferenceGenerator"/>) sits alongside as <c>expected.html</c> / <c>expected.md</c>
/// for visual comparison.
///
/// A curated, representative subset of the ~320 inputs is used so the committed baselines stay
/// reviewable; <see cref="PandocReferenceGenerator"/> can seed the full corpus on demand.
/// </summary>
public class ExportScenarioTests
{
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
    public Task Html(string directory)
    {
        var html = DocumentConverter.ConvertToHtml(Path.Combine(directory, "input.docx"));
        return Verify(html)
            .UseDirectory(directory)
            .UseFileName("results_html")
            .IgnoreParameters();
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public Task Markdown(string directory)
    {
        var markdown = DocumentConverter.ConvertToMarkdown(Path.Combine(directory, "input.docx"));
        return Verify(markdown)
            .UseDirectory(directory)
            .UseFileName("results_markdown")
            .IgnoreParameters();
    }
}
