/// <summary>
/// Generates a side-by-side markdown preview (compare.md) for a scenario directory,
/// showing Expected vs Skia vs ImageSharp verified page renders. One table row per
/// page, so multi-page scenarios (including ones where backends produced different
/// page counts) can be scrolled through. Renders cleanly on GitHub.
///
/// Also generates an aggregate compare-all.md at the Inputs root that strings
/// every scenario together for scrolling through the full corpus in one view.
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

        File.WriteAllText(Path.Combine(directory, "compare.md"), sb.ToString());
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

        File.WriteAllText(Path.Combine(inputsDirectory, "compare-all.md"), sb.ToString());
    }

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
            sb.Append("| ");
            sb.Append(RenderCell(pageLabel, null, page.ExpectedFile, srcPrefix));
            sb.Append(" | ");
            sb.Append(RenderCell(pageLabel, page.SkiaMetric, page.SkiaFile, srcPrefix));
            sb.Append(" | ");
            sb.Append(RenderCell(pageLabel, page.ImageSharpMetric, page.ImageSharpFile, srcPrefix));
            sb.Append(" |\n");
        }
    }

    static string RenderCell(string pageLabel, double? metric, string? fileName, string srcPrefix)
    {
        var label = metric.HasValue
            ? $"{pageLabel}. ErrorMetric: {metric.Value:F4}"
            : pageLabel;

        if (fileName == null)
        {
            return $"**{label}**<br>_(no page)_";
        }

        var src = (srcPrefix + fileName).Replace("#", "%23");
        // Use an explicit width <img> rather than ![]() so all three columns get
        // identical image sizes — markdown renderers otherwise size columns by
        // label text width and shrink images to fit, making the Expected column
        // (which has no ErrorMetric suffix) render noticeably smaller.
        return $"""**{label}**<br><img src="{src}" width="500">""";
    }

    static List<PageRow> CollectPages(string directory)
    {
        var expectedFiles = Directory.GetFiles(directory, "expected_*.png").Order().ToArray();
        var skiaFiles = Directory.GetFiles(directory, "results_skia#page_*.verified.png").Order().ToArray();
        var imagesharpFiles = Directory.GetFiles(directory, "results_imagesharp#page_*.verified.png").Order().ToArray();

        var skiaMetrics = ReadMetrics(Path.Combine(directory, "results_skia.verified.json"));
        var imagesharpMetrics = ReadMetrics(Path.Combine(directory, "results_imagesharp.verified.json"));

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
