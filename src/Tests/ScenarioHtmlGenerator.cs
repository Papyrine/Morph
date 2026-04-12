#if DEBUG
using System.Text;
using System.Text.Json;

/// <summary>
/// Generates a side-by-side HTML preview (compare.html) for a scenario directory,
/// showing Expected vs Skia vs ImageSharp verified page renders. One table row per
/// page, so multi-page scenarios (including ones where backends produced different
/// page counts) can be scrolled through.
/// </summary>
static class ScenarioHtmlGenerator
{
    public static void Regenerate(string directory)
    {
        var expectedFiles = Directory.GetFiles(directory, "expected_*.png").Order().ToArray();
        var skiaFiles = Directory.GetFiles(directory, "results_skia#page_*.verified.png").Order().ToArray();
        var imagesharpFiles = Directory.GetFiles(directory, "results_imagesharp#page_*.verified.png").Order().ToArray();

        var skiaMetrics = ReadMetrics(Path.Combine(directory, "results_skia.verified.json"));
        var imagesharpMetrics = ReadMetrics(Path.Combine(directory, "results_imagesharp.verified.json"));

        var maxPages = Math.Max(expectedFiles.Length, Math.Max(skiaFiles.Length, imagesharpFiles.Length));
        if (maxPages == 0)
        {
            return;
        }

        var scenarioName = GetScenarioName(directory);

        var sb = new StringBuilder();
        sb.AppendLine($$"""
            <!DOCTYPE html>
            <html><head>
            <title>{{HtmlEncode(scenarioName)}}</title>
            <style>
              body { background: #2a2a2a; color: #fff; font-family: -apple-system, Segoe UI, sans-serif; margin: 20px; }
              h1 { text-align: center; margin-bottom: 20px; }
              table { border-collapse: collapse; margin: 0 auto; }
              thead th { position: sticky; top: 0; background: #1a1a1a; padding: 12px 20px; font-size: 18px; border-bottom: 2px solid #555; }
              td { padding: 16px; vertical-align: top; text-align: center; }
              td img { max-width: 500px; max-height: 700px; border: 2px solid #666; background: white; display: block; }
              .page-label { font-weight: bold; margin-bottom: 6px; font-size: 14px; }
              .missing { color: #888; font-style: italic; padding: 80px 40px; border: 2px dashed #555; min-width: 400px; min-height: 500px; display: flex; align-items: center; justify-content: center; }
              tr { border-bottom: 1px solid #444; }
            </style>
            </head><body>
            <h1>{{HtmlEncode(scenarioName)}}</h1>
            <table>
            <thead><tr><th>Expected (Word)</th><th>Skia</th><th>ImageSharp</th></tr></thead>
            <tbody>
            """);

        for (var i = 0; i < maxPages; i++)
        {
            var pageLabel = $"Page {i + 1}";
            sb.AppendLine("<tr>");
            sb.AppendLine(RenderCell(pageLabel, null, i < expectedFiles.Length ? Path.GetFileName(expectedFiles[i]) : null));
            sb.AppendLine(RenderCell(
                pageLabel,
                skiaMetrics.GetValueOrDefault(i + 1),
                i < skiaFiles.Length ? Path.GetFileName(skiaFiles[i]) : null));
            sb.AppendLine(RenderCell(
                pageLabel,
                imagesharpMetrics.GetValueOrDefault(i + 1),
                i < imagesharpFiles.Length ? Path.GetFileName(imagesharpFiles[i]) : null));
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("""
            </tbody></table>
            </body></html>
            """);

        File.WriteAllText(Path.Combine(directory, "compare.html"), sb.ToString());
    }

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

    static string RenderCell(string pageLabel, double? metric, string? fileName)
    {
        var label = metric.HasValue
            ? $"{pageLabel}. ErrorMetric: {metric.Value:F4}"
            : pageLabel;

        if (fileName == null)
        {
            return $"""<td><div class="page-label">{label}</div><div class="missing">(no page)</div></td>""";
        }

        var src = fileName.Replace("#", "%23");
        return $"""<td><div class="page-label">{label}</div><img src="{src}"></td>""";
    }

    static string GetScenarioName(string directory)
    {
        var inputsDir = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        if (directory.StartsWith(inputsDir, StringComparison.OrdinalIgnoreCase))
        {
            return directory.Substring(inputsDir.Length).TrimStart('\\', '/').Replace('\\', '/');
        }
        return Path.GetFileName(directory);
    }

    static string HtmlEncode(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
#endif
