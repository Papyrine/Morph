using SkiaSharp;

/// <summary>
/// Integration gate for step 1 of the layout engine (<c>docs/layout-engine.md</c>): does the
/// backend-independent <see cref="CanonicalTextMeasurer"/> break real corpus paragraphs at the same
/// lines the raster backend's own font engine (SkiaSharp) does? SkiaSharp matches Word on ~97% of the
/// corpus, so high agreement here is evidence the canonical metric model reproduces Word's line
/// breaking — the one claim step 1's unit tests could not check on their own. Both sides run the same
/// greedy wrap; only the width source differs (canonical hhea/hmtx + ppem quantization vs SkiaSharp's
/// MeasureText), so a disagreement isolates exactly where the models part.
/// </summary>
public class CanonicalWrapAgreementTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));
    static readonly string inputsDirectory = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word");

    static readonly FontFileCache fonts = new(
        FontCacheLoader.EnumerateFontFilesInDirectory(fontsDirectory, recursive: true),
        OpenTypeReader.ReadFaces);

    static string? ResolvePath(string family, bool bold, bool italic)
    {
        var candidates = FontHelpers.GetCandidateNames(family, bold);
        if (!fonts.TryGet(candidates, out var faces) || faces.Length == 0)
        {
            return null;
        }

        var weight = FontHelpers.ResolveTargetWeight(family, bold);
        return faces.OrderBy(_ => FontHelpers.ScoreFace(_, weight, italic)).First().Path;
    }

    [Test]
    public async Task Canonical_wrap_agrees_with_the_raster_backend_on_corpus_paragraphs()
    {
        // EVERY input, not a sample. This used to take every 3rd of the first 120 in ordinal path
        // order, which made the measured population depend on corpus MEMBERSHIP and on the PLATFORM:
        // adding `border_style_variants` shifted the stride onto a different 120 documents and the
        // rate read 96.2% against the 97% gate, while the same tree passed on Windows because
        // ordinal ordering sorts '\' and '/' differently and the two OSes therefore sampled
        // different sets. Nothing about the measurer had changed either time. Over the whole corpus
        // the rate clears the gate, so the subsample was also hiding the stronger result it was
        // meant to approximate. 325 inputs cost ~11s against the subsample's ~4s, which is free
        // beside a ~2m40s suite this runs inside.
        var inputs = Directory.GetFiles(inputsDirectory, "input.docx", SearchOption.AllDirectories)
            .OrderBy(_ => _, StringComparer.Ordinal)
            .ToList();

        var typefaces = new Dictionary<string, SKTypeface?>();
        SKTypeface? Typeface(string path) =>
            typefaces.TryGetValue(path, out var cached) ? cached : typefaces[path] = SKTypeface.FromFile(path);

        var metricsByPath = new Dictionary<string, FontMetrics?>();
        FontMetrics? Metrics(string path) =>
            metricsByPath.TryGetValue(path, out var cached) ? cached : metricsByPath[path] = FontMetricsReader.Read(path);

        var compared = 0;
        var agree = 0;
        var disagreements = new List<string>();

        foreach (var input in inputs)
        {
            ParsedDocument document;
            try
            {
                await using var stream = File.OpenRead(input);
                document = new DocumentParser().Parse(stream);
            }
            catch
            {
                // a parse failure isn't what this gate measures
                continue;
            }

            var contentWidth = (float) document.PageSettings.ContentWidth;
            foreach (var paragraph in UniformBodyParagraphs(document.Elements))
            {
                var first = paragraph.Runs.FirstOrDefault(_ => !string.IsNullOrWhiteSpace(_.Text));
                if (first == null)
                {
                    continue;
                }

                var props = first.Properties;
                var text = string.Concat(paragraph.Runs.Where(_ => !string.IsNullOrEmpty(_.Text)).Select(_ => _.Text));
                var width = contentWidth - (float) paragraph.Properties.LeftIndentPoints - (float) paragraph.Properties.RightIndentPoints;
                if (width < 40 || text.Trim().Length < 60 || string.IsNullOrEmpty(props.FontFamily))
                {
                    continue;
                }

                var path = ResolvePath(props.FontFamily, props.Bold, props.Italic);
                var metrics = path == null ? null : Metrics(path);
                var typeface = path == null ? null : Typeface(path);
                if (metrics == null || typeface == null || metrics.AdvanceWidths.Count == 0)
                {
                    continue;
                }

                var size = props.FontSizePoints;
                var canonicalLines = CanonicalTextMeasurer.WrapLines(metrics, text, size, width).Count;
                var skiaLines = SkiaLineCount(typeface, (float) size, text, width);

                compared++;
                if (canonicalLines == skiaLines)
                {
                    agree++;
                }
                else if (disagreements.Count < 15)
                {
                    disagreements.Add($"{Path.GetFileName(Path.GetDirectoryName(input))}: canonical={canonicalLines} skia={skiaLines} '{props.FontFamily}' {size}pt w={width:0}  \"{Trim(text)}\"");
                }
            }
        }

        var rate = compared == 0 ? 0 : (double) agree / compared;
        Console.WriteLine($"Canonical vs Skia wrap: {agree}/{compared} paragraphs agree ({rate:P1}).");
        foreach (var line in disagreements)
        {
            Console.WriteLine("  DIFF " + line);
        }

        await Assert.That(compared).IsGreaterThan(100);
        // Measured at ~99.3% on this sample once the measurer switched to pen-position rounding (the
        // whole-line total tracks the linear ideal, so per-glyph error can't accumulate and over-wrap).
        // The lone residual is one Calibri 10pt paragraph that sits a sub-pixel from its wrap boundary.
        // Agreement above 97% is the signal that the canonical model reproduces the backend's (hence
        // Word's) line breaking.
        await Assert.That(rate > 0.97).IsTrue();
    }

    // Paragraphs whose every visible run shares one font family, size, weight and slant — so a single
    // canonical measurement is a faithful stand-in — and which carry no tabs, breaks, fields or inline
    // images that the plain greedy wrap doesn't model.
    static IEnumerable<ParagraphElement> UniformBodyParagraphs(IReadOnlyList<DocumentElement> elements)
    {
        foreach (var element in elements)
        {
            if (element is not ParagraphElement paragraph)
            {
                continue;
            }

            var visible = paragraph.Runs.Where(_ => !string.IsNullOrEmpty(_.Text) && !_.IsTab).ToList();
            if (visible.Count == 0 ||
                paragraph.Runs.Any(_ => _.IsTab ||
                                        _.InlineImageData != null ||
                                        _.PageField != PageFieldKind.None ||
                                        _.Text.Contains('\n')) ||
                paragraph.Properties.Numbering != null)
            {
                continue;
            }

            var reference = visible[0].Properties;
            var uniform = visible.All(_ =>
                string.Equals(_.Properties.FontFamily, reference.FontFamily, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(_.Properties.FontSizePoints - reference.FontSizePoints) < 0.01 &&
                _.Properties.Bold == reference.Bold &&
                _.Properties.Italic == reference.Italic);
            if (uniform)
            {
                yield return paragraph;
            }
        }
    }

    static int SkiaLineCount(SKTypeface typeface, float sizePoints, string text, float maxWidthPoints)
    {
        using var font = new SKFont(typeface, sizePoints);
        var spaceWidth = font.MeasureText(" ");
        var lines = 0;
        foreach (var segment in text.Split('\n'))
        {
            var currentWidth = 0f;
            var started = false;
            foreach (var word in segment.Split(' '))
            {
                var wordWidth = font.MeasureText(word);
                if (!started)
                {
                    currentWidth = wordWidth;
                    started = true;
                }
                else if (currentWidth + spaceWidth + wordWidth <= maxWidthPoints)
                {
                    currentWidth += spaceWidth + wordWidth;
                }
                else
                {
                    lines++;
                    currentWidth = wordWidth;
                }
            }

            lines++;
        }

        return lines;
    }

    static string Trim(string text) => text.Length <= 50 ? text : text[..50] + "…";
}
