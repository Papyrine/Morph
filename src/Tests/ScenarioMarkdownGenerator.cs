/// <summary>
/// Generates side-by-side markdown previews for the scenario corpus, two flavours:
///
/// - Image rendering: per-scenario <c>compare.md</c> plus an aggregate
///   <c>compare-all-images.md</c>, showing Expected (Word) vs Skia vs ImageSharp verified
///   page renders. One table row per page, so multi-page scenarios (including ones where
///   backends produced different page counts) can be scrolled through.
/// - Export: an aggregate <c>compare-all-export.md</c> showing the HTML, Markdown and PDF
///   exporters against the Pandoc reference (see <see cref="RegenerateAllExport"/>).
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
        sb.Append("# ").Append(scenarioName).Append("\n\n");
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
        sb.Append("# All scenarios (").Append(scenarios.Length).Append(")\n\n");

        sb.Append("## Contents\n\n");
        foreach (var (_, name, _) in scenarios)
        {
            sb.Append("- [").Append(name).Append("](#").Append(GitHubAnchor(name)).Append(")\n");
        }
        sb.Append('\n');

        foreach (var (dir, name, pages) in scenarios)
        {
            sb.Append("## ").Append(name).Append("\n\n");
            AppendNotes(sb, dir);
            AppendTable(sb, pages, srcPrefix: $"{name}/");
            sb.Append('\n');
        }

        File.WriteAllText(Path.Combine(inputsDirectory, "compare-all-images.md"), sb.ToString());
    }

    /// <summary>
    /// Generates the aggregate <c>compare-all-export.md</c> at the Inputs root, the export-pipeline
    /// counterpart to <see cref="RegenerateAll"/>. For each scenario it lays the HTML and Markdown
    /// exporters side by side against the Pandoc reference (<c>expected.html.png</c>), all rendered
    /// to PNG by the headless-browser screenshot pipeline, followed by the PDF exporter's per-page
    /// renders (<c>pdf_result#page_*.verified.png</c>, produced by Verify.PDFium). The Pandoc
    /// <c>expected.pdf</c> reference has no raster and is linked as a file.
    /// </summary>
    public static void RegenerateAllExport(string inputsDirectory)
    {
        var scenarios = Directory.GetFiles(inputsDirectory, "input.docx", SearchOption.AllDirectories)
            .Select(_ => Path.GetDirectoryName(_)!)
            .OrderBy(_ => _, StringComparer.OrdinalIgnoreCase)
            .Select(_ => (Directory: _, Name: GetScenarioName(_)))
            .Where(_ => File.Exists(Path.Combine(_.Directory, "html_result.verified.png")) ||
                        File.Exists(Path.Combine(_.Directory, "md_result.verified.png")))
            .ToArray();

        if (scenarios.Length == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append("# All export scenarios (").Append(scenarios.Length).Append(")\n\n");
        sb.Append("The HTML, Markdown and PDF exporters side by side against the Pandoc reference. ");
        sb.Append("HTML and Markdown are rendered to PNG via the headless-browser screenshot pipeline; ");
        sb.Append("PDF pages are rendered by PDFium (Verify.PDFium). ");
        sb.Append("The Pandoc expected.pdf reference has no raster, so it is linked as a file.\n\n");

        sb.Append("## Contents\n\n");
        foreach (var (_, name) in scenarios)
        {
            sb.Append("- [").Append(name).Append("](#").Append(GitHubAnchor(name)).Append(")\n");
        }
        sb.Append('\n');

        foreach (var (dir, name) in scenarios)
        {
            sb.Append("## ").Append(name).Append("\n\n");
            AppendExportTable(sb, dir, name);
            sb.Append('\n');
        }

        File.WriteAllText(Path.Combine(inputsDirectory, "compare-all-export.md"), sb.ToString());
    }

    static void AppendExportTable(StringBuilder sb, string directory, string name)
    {
        var srcPrefix = $"{name}/";

        sb.Append("| Reference (Pandoc HTML) | Morph HTML | Morph Markdown |\n");
        sb.Append("| --- | --- | --- |\n");
        sb.Append("| ");
        sb.Append(RenderImage(FileNameIfExists(directory, "expected.html.png"), srcPrefix));
        sb.Append(" | ");
        sb.Append(RenderImage(FileNameIfExists(directory, "html_result.verified.png"), srcPrefix));
        sb.Append(" | ");
        sb.Append(RenderImage(FileNameIfExists(directory, "md_result.verified.png"), srcPrefix));
        sb.Append(" |\n\n");

        AppendPdf(sb, directory, srcPrefix);
    }

    static void AppendPdf(StringBuilder sb, string directory, string srcPrefix)
    {
        // Per-page renders of the PdfSharp output, produced by Verify.PDFium during
        // ExportScenarioTests.PdfOutput. The file links stay alongside so the actual
        // pdf (and the raster-less Pandoc reference) remain one click away.
        var pdfPages = Directory.GetFiles(directory, "pdf_result#page_*.verified.png").Order().ToArray();
        if (pdfPages.Length > 0)
        {
            sb.Append("| Morph PDF |\n| --- |\n");
            foreach (var page in pdfPages)
            {
                sb.Append("| ").Append(RenderImage(Path.GetFileName(page), srcPrefix)).Append(" |\n");
            }
            sb.Append('\n');
        }

        var links = new List<string>();
        if (File.Exists(Path.Combine(directory, "pdf_result.verified.pdf")))
        {
            links.Add($"[Morph PDF]({EncodeSrc(srcPrefix + "pdf_result.verified.pdf")})");
        }
        if (File.Exists(Path.Combine(directory, "expected.pdf")))
        {
            links.Add($"[Pandoc reference]({EncodeSrc(srcPrefix + "expected.pdf")})");
        }
        if (links.Count > 0)
        {
            sb.Append("PDF: ").Append(string.Join(" · ", links)).Append("\n\n");
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
            sb.Append(RenderLabel(pageLabel, null, page.ExpectedFile));
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
