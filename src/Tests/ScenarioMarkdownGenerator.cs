/// <summary>
/// Generates side-by-side markdown previews for the scenario corpus, two flavours:
///
/// - Image rendering: per-scenario <c>compare.md</c> plus an aggregate
///   <c>compare-all-images.md</c>, showing Expected (Word) vs Skia vs ImageSharp verified
///   page renders. One table row per page, so multi-page scenarios (including ones where
///   backends produced different page counts) can be scrolled through.
/// - Export: a per-format aggregate each — <c>compare-all-html.md</c>,
///   <c>compare-all-markdown.md</c>, <c>compare-all-pdf.md</c>. The HTML and Markdown aggregates
///   are a visual index of each exporter's render; the PDF aggregate sets each Morph PDF page
///   beside the Word page render (see <see cref="RegenerateAllExport"/>).
///
/// All render cleanly on GitHub.
/// </summary>
static class ScenarioMarkdownGenerator
{
    public static void Regenerate(string directory)
    {
        var pages = CollectPages(directory);
        if (pages.Count == 0)
        {
            return;
        }

        var scenarioName = GetScenarioName(directory);

        var sb = new StringBuilder();
        sb.Append($"# {scenarioName}\n\n");
        AppendNotes(sb, directory);
        AppendTable(sb, pages, srcPrefix: "");

        // Skia and ImageSharp scenarios for the same dir run concurrently and both regenerate
        // compare.md; on Windows a contended write can fail with "user-mapped section open" /
        // "in use". Swallow it — the second backend's content matches the first (same source),
        // and the ModuleInitializer ProcessExit hook runs RegenerateAll at the end as the final
        // reconciliation pass.
        try
        {
            File.WriteAllText(Path.Combine(directory, "compare.md"), sb.ToString());
        }
        catch (IOException)
        {
        }
    }

    public static void RegenerateAll(string inputsDirectory)
    {
        var scenarioDirs = Directory.GetFiles(inputsDirectory, "input.docx", SearchOption.AllDirectories)
            .Select(_ => Path.GetDirectoryName(_)!)
            .OrderBy(_ => _, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scenarioDirs.Length == 0)
        {
            return;
        }

        var scenarios = scenarioDirs
            .Select(_ => (Directory: _, Name: GetScenarioName(_), Pages: CollectPages(_)))
            .Where(_ => _.Pages.Count > 0)
            .ToArray();

        if (scenarios.Length == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"# All scenarios ({scenarios.Length})\n\n");

        AppendContents(sb, scenarios.Select(_ => _.Name));

        foreach (var (dir, name, pages) in scenarios)
        {
            sb.Append($"## {name}\n\n");
            AppendNotes(sb, dir);
            AppendTable(sb, pages, srcPrefix: $"{name}/");
            sb.Append('\n');
        }

        File.WriteAllText(Path.Combine(inputsDirectory, "compare-all-images.md"), sb.ToString());
    }

    /// <summary>
    /// Generates the per-format export aggregates at the Inputs root, the export-pipeline
    /// counterparts to <see cref="RegenerateAll"/>:
    /// <list type="bullet">
    /// <item><c>compare-all-html.md</c> — a visual index of the HTML exporter's renders</item>
    /// <item><c>compare-all-markdown.md</c> — a visual index of the Markdown exporter's renders</item>
    /// <item><c>compare-all-pdf.md</c> — each Morph PDF page beside the Word page render</item>
    /// </list>
    /// HTML and Markdown render to PNG via the headless-browser screenshot pipeline; PDF pages
    /// are rendered by PDFium (Verify.PDFium).
    /// </summary>
    public static void RegenerateAllExport(string inputsDirectory)
    {
        WriteExportAggregate(
            inputsDirectory,
            "compare-all-html.md",
            "All HTML export scenarios",
            "The HTML exporter rendered to PNG via the headless-browser screenshot pipeline.",
            _ => File.Exists(Path.Combine(_, "html_result.verified.png")),
            (sb, dir, name) => AppendRender(sb, dir, name, "Morph HTML", "html_result.verified.png"));

        WriteExportAggregate(
            inputsDirectory,
            "compare-all-markdown.md",
            "All Markdown export scenarios",
            "The Markdown exporter rendered to PNG via the headless-browser screenshot pipeline.",
            _ => File.Exists(Path.Combine(_, "md_result.verified.png")),
            (sb, dir, name) => AppendRender(sb, dir, name, "Morph Markdown", "md_result.verified.png"));

        WriteExportAggregate(
            inputsDirectory,
            "compare-all-pdf.md",
            "All PDF export scenarios",
            "The Word reference render (left) beside each Morph PDF page rendered by PDFium (Verify.PDFium).",
            _ => Directory.GetFiles(_, "pdf_result#page_*.verified.png").Length > 0 ||
                 File.Exists(Path.Combine(_, "pdf_result.verified.pdf")),
            AppendPdf);
    }

    static void WriteExportAggregate(
        string inputsDirectory,
        string fileName,
        string title,
        string intro,
        Func<string, bool> hasContent,
        Action<StringBuilder, string, string> appendBody)
    {
        var scenarios = Directory.GetFiles(inputsDirectory, "input.docx", SearchOption.AllDirectories)
            .Select(_ => Path.GetDirectoryName(_)!)
            .OrderBy(_ => _, StringComparer.OrdinalIgnoreCase)
            .Select(_ => (Directory: _, Name: GetScenarioName(_)))
            .Where(_ => hasContent(_.Directory))
            .ToArray();

        if (scenarios.Length == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"# {title} ({scenarios.Length})\n\n");
        sb.Append(intro).Append("\n\n");

        AppendContents(sb, scenarios.Select(_ => _.Name));

        foreach (var (dir, name) in scenarios)
        {
            sb.Append($"## {name}\n\n");
            appendBody(sb, dir, name);
            sb.Append('\n');
        }

        File.WriteAllText(Path.Combine(inputsDirectory, fileName), sb.ToString());
    }

    // A single-column visual index: this format's rendered output, one row per scenario.
    static void AppendRender(StringBuilder sb, string directory, string name, string resultLabel, string resultFile)
    {
        var srcPrefix = $"{name}/";

        sb.Append($"| {resultLabel} |\n");
        sb.Append("| --- |\n");
        sb.Append("| ");
        sb.Append(RenderImage(FileNameIfExists(directory, resultFile), srcPrefix));
        sb.Append(" |\n");
    }

    static void AppendPdf(StringBuilder sb, string directory, string name)
    {
        var srcPrefix = $"{name}/";

        // Pair each Word reference page (rendered by Word via COM interop — the same expected_*.png
        // the image aggregate uses) on the left with the matching Morph PDF page (rendered by
        // PDFium during ExportScenarioTests.PdfOutput) on the right, one row per page — mirroring
        // compare-all-images.md. Page counts can differ between the two, so rows run to the longer
        // side and the short side shows a blank cell. The Morph PDF file link stays alongside so
        // the actual pdf is one click away.
        var expectedPages = Directory.GetFiles(directory, "expected_*.png").Order().ToArray();
        var pdfPages = Directory.GetFiles(directory, "pdf_result#page_*.verified.png").Order().ToArray();
        var pdfMetrics = ReadMetrics(Path.Combine(directory, "pdf_result.verified.json"));
        if (expectedPages.Length > 0 || pdfPages.Length > 0)
        {
            sb.Append("| Expected (Word) | Morph PDF |\n");
            sb.Append("| --- | --- |\n");
            var maxPages = Math.Max(expectedPages.Length, pdfPages.Length);
            for (var i = 0; i < maxPages; i++)
            {
                var expectedFile = i < expectedPages.Length ? Path.GetFileName(expectedPages[i]) : null;
                var pdfFile = i < pdfPages.Length ? Path.GetFileName(pdfPages[i]) : null;
                var pageLabel = $"Page {i + 1}";
                // No PageDiffs when PDF and Word page counts differ; show the page label alone then.
                var pdfMetric = pdfMetrics.TryGetValue(i + 1, out var metric) ? metric : (double?) null;

                sb.Append("| ");
                sb.Append(RenderLabel(pageLabel, null, expectedFile));
                sb.Append(" | ");
                sb.Append(RenderLabel(pageLabel, pdfMetric, pdfFile));
                sb.Append(" |\n");

                sb.Append("| ");
                sb.Append(RenderImage(expectedFile, srcPrefix));
                sb.Append(" | ");
                sb.Append(RenderImage(pdfFile, srcPrefix));
                sb.Append(" |\n");
            }
            sb.Append('\n');
        }

        if (File.Exists(Path.Combine(directory, "pdf_result.verified.pdf")))
        {
            sb.Append("PDF: ").Append($"[Morph PDF]({EncodeSrc(srcPrefix + "pdf_result.verified.pdf")})").Append("\n\n");
        }
    }

    static string? FileNameIfExists(string directory, string fileName) =>
        File.Exists(Path.Combine(directory, fileName)) ? fileName : null;

    static void AppendNotes(StringBuilder sb, string directory)
    {
        var notesPath = Path.Combine(directory, "notes.md");
        if (!File.Exists(notesPath))
        {
            return;
        }

        var content = File.ReadAllText(notesPath).Trim();
        if (content.Length == 0)
        {
            return;
        }

        sb.Append(content).Append("\n\n");
    }

    static void AppendTable(StringBuilder sb, List<PageRow> pages, string srcPrefix)
    {
        sb.Append("| Expected (Word) | Skia | ImageSharp |\n");
        sb.Append("| --- | --- | --- |\n");
        foreach (var page in pages)
        {
            var pageLabel = $"Page {page.PageNumber}";
            // One row of labels (page + ErrorMetric) and a separate row for the images
            // beneath, so each cell's text and image stack vertically inside the table.
            sb.Append("| ");
            sb.Append(RenderExpectedLabel(pageLabel, page));
            sb.Append(" | ");
            sb.Append(RenderLabel(pageLabel, page.SkiaMetric, page.SkiaFile));
            sb.Append(" | ");
            sb.Append(RenderLabel(pageLabel, page.ImageSharpMetric, page.ImageSharpFile));
            sb.Append(" |\n");

            sb.Append("| ");
            sb.Append(RenderImage(page.ExpectedFile, srcPrefix));
            sb.Append(" | ");
            sb.Append(RenderImage(page.SkiaFile, srcPrefix));
            sb.Append(" | ");
            sb.Append(RenderImage(page.ImageSharpFile, srcPrefix));
            sb.Append(" |\n");
        }
    }

    // The scenario index is long, so collapse it behind a <details> disclosure (collapsed by
    // default on GitHub). The blank lines around the list are required for GitHub to parse the
    // markdown links inside the HTML block.
    static void AppendContents(StringBuilder sb, IEnumerable<string> names)
    {
        sb.Append("<details>\n<summary>Contents</summary>\n\n");
        foreach (var name in names)
        {
            sb.Append($"- [{name}](#{GitHubAnchor(name)})\n");
        }
        sb.Append("\n</details>\n\n");
    }

    static string RenderLabel(string pageLabel, double? metric, string? fileName)
    {
        if (fileName == null)
        {
            return $"**{pageLabel}** _(no page)_";
        }

        var label = metric.HasValue
            ? $"{pageLabel}. ErrorMetric: {metric.Value:F4}"
            : pageLabel;
        return $"**{label}**";
    }

    // GitHub sizes each table column to its widest non-breaking token, and images carry
    // max-width:100% so they can never widen a column. The Skia/ImageSharp columns are
    // sized by their "ErrorMetric: 0.xxxx" label, but the Expected column's bare "Page N"
    // is far narrower — so GitHub shrank the Expected image relative to the other two.
    // Pad the Expected label with non-breaking spaces so its token matches the ErrorMetric
    // label width, equalizing all three columns (and therefore their images). The count was
    // calibrated against GitHub's table CSS in a browser; because the columns are sized by
    // min-content it is independent of the reader's viewport width.
    static readonly string expectedLabelPadding = string.Concat(Enumerable.Repeat("&nbsp;", 19));

    static string RenderExpectedLabel(string pageLabel, PageRow page)
    {
        var label = RenderLabel(pageLabel, null, page.ExpectedFile);
        // Only pad when a sibling column actually renders an ErrorMetric label and there is
        // an Expected image to size — otherwise the columns already match and padding would
        // over-widen this one.
        if (page.ExpectedFile != null && (page.SkiaMetric.HasValue || page.ImageSharpMetric.HasValue))
        {
            return label + expectedLabelPadding;
        }

        return label;
    }

    static string RenderImage(string? fileName, string srcPrefix)
    {
        if (fileName == null)
        {
            return "";
        }

        // Use an explicit width <img> rather than ![]() so all three columns get
        // identical image sizes — markdown renderers otherwise size columns by
        // text width and shrink images to fit.
        return $"""<img src="{EncodeSrc(srcPrefix + fileName)}" width="500">""";
    }

    static string EncodeSrc(string path) => path.Replace("#", "%23");

    static List<PageRow> CollectPages(string directory)
    {
        var expectedFiles = Directory.GetFiles(directory, "expected_*.png").Order().ToArray();
        var skiaFiles = Directory.GetFiles(directory, "skia_result#page_*.verified.png").Order().ToArray();
        var imagesharpFiles = Directory.GetFiles(directory, "imagesharp_result#page_*.verified.png").Order().ToArray();

        var skiaMetrics = ReadMetrics(Path.Combine(directory, "skia_result.verified.json"));
        var imagesharpMetrics = ReadMetrics(Path.Combine(directory, "imagesharp_result.verified.json"));

        var maxPages = Math.Max(expectedFiles.Length, Math.Max(skiaFiles.Length, imagesharpFiles.Length));
        var rows = new List<PageRow>(maxPages);
        for (var i = 0; i < maxPages; i++)
        {
            rows.Add(new(
                i + 1,
                i < expectedFiles.Length ? Path.GetFileName(expectedFiles[i]) : null,
                i < skiaFiles.Length ? Path.GetFileName(skiaFiles[i]) : null,
                skiaMetrics.GetValueOrDefault(i + 1),
                i < imagesharpFiles.Length ? Path.GetFileName(imagesharpFiles[i]) : null,
                imagesharpMetrics.GetValueOrDefault(i + 1)));
        }
        return rows;
    }

    record PageRow(
        int PageNumber,
        string? ExpectedFile,
        string? SkiaFile,
        double? SkiaMetric,
        string? ImageSharpFile,
        double? ImageSharpMetric);

    static Dictionary<int, double> ReadMetrics(string jsonPath)
    {
        var result = new Dictionary<int, double>();
        if (!File.Exists(jsonPath))
        {
            return result;
        }

        var json = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("PageDiffs", out var diffs) || diffs.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var diff in diffs.EnumerateArray())
        {
            if (diff.TryGetProperty("Page", out var pageEl) &&
                diff.TryGetProperty("ErrorMetric", out var metricEl))
            {
                result[pageEl.GetInt32()] = metricEl.GetDouble();
            }
        }

        return result;
    }

    static string GetScenarioName(string directory)
    {
        var inputsDir = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs"));
        var full = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(inputsDir, full);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return Path.GetFileName(directory);
        }
        return relative.Replace('\\', '/');
    }

    // Mirrors GitHub's heading-anchor algorithm: lowercase, drop punctuation,
    // collapse whitespace to '-'. Underscores are kept.
    static string GitHubAnchor(string heading)
    {
        var sb = new StringBuilder(heading.Length);
        foreach (var ch in heading)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '-' or '_')
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append('-');
            }
        }
        return sb.ToString();
    }
}
