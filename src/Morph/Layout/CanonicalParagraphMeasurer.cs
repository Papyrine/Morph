/// <summary>
/// The <see cref="IParagraphMeasurer"/> surface over the backend-independent
/// <see cref="CanonicalTextMeasurer"/> — step 1 of the layout engine
/// (<c>docs/layout-engine-proposal.md</c>). It wraps a paragraph's runs and reports line heights,
/// natural width and total height from the font's own OpenType metrics, so the shared table-height
/// math (and, later, the fragmenter) can measure without a backend font library. Font resolution is
/// injected as a delegate — the caller wires a <c>FontResolver&lt;FontMetrics&gt;</c> — keeping this
/// class purely about layout.
///
/// <para>Modelled: multi-run greedy wrap (adjacent non-space pieces across runs form one word, so a
/// mid-word format change never splits the word), per-line height = the tallest run's hhea box under
/// the paragraph's line-spacing rule, and Word's before/after spacing. Not yet modelled — deferred to
/// later layout work: tabs, first-line / hanging indents (only the block left/right indent is applied),
/// and the empty-paragraph mark's exact style (approximated by the first run's font).</para>
/// </summary>
sealed class CanonicalParagraphMeasurer(Func<string, bool, bool, FontMetrics?> resolveFont) : IParagraphMeasurer
{
    public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth)
    {
        var props = paragraph.Properties;
        var lines = Wrap(paragraph, maxWidth);
        var heights = new List<float>(lines.Count);
        foreach (var line in lines)
        {
            heights.Add((float) CanonicalTextMeasurer.LineHeightPoints(
                line.Pitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints));
        }

        return heights;
    }

    public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth)
    {
        var widest = 0f;
        foreach (var line in Wrap(paragraph, maxWidth))
        {
            if (line.Width > widest)
            {
                widest = line.Width;
            }
        }

        return widest;
    }

    public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth)
    {
        var props = paragraph.Properties;
        var total = (float) props.SpacingBeforePoints;
        var lines = Wrap(paragraph, maxWidth);
        foreach (var line in lines)
        {
            total += (float) CanonicalTextMeasurer.LineHeightPoints(
                line.Pitch, props.LineSpacingRule, props.LineSpacingMultiplier, props.LineSpacingPoints);
        }

        // An empty paragraph is a visual spacer — Word emits its mark's line but not the after-spacing.
        if (lines.Count != 1 || lines[0].Width > 0)
        {
            total += (float) props.SpacingAfterPoints;
        }

        return total;
    }

    // Pixels is the piece's unrounded linear device-pixel advance; accumulating it per line and
    // quantizing once (PixelsToPoints) is pen-position rounding, which composes across fonts.
    readonly record struct Piece(bool IsSpace, double Pixels, float Pitch);

    IReadOnlyList<(float Width, float Pitch)> Wrap(ParagraphElement paragraph, float maxWidth)
    {
        var pieces = Flatten(paragraph);
        var wrapWidth = maxWidth - (float) paragraph.Properties.LeftIndentPoints - (float) paragraph.Properties.RightIndentPoints;

        var lines = new List<(float Width, float Pitch)>();
        double linePixels = 0, gapPixels = 0;
        float linePitch = 0, gapPitch = 0;
        var lineHasWord = false;

        var index = 0;
        while (index < pieces.Count)
        {
            if (pieces[index].IsSpace)
            {
                gapPixels += pieces[index].Pixels;
                gapPitch = Math.Max(gapPitch, pieces[index].Pitch);
                index++;
                continue;
            }

            // Gather the whole word — every adjacent non-space piece, even across a run boundary.
            double wordPixels = 0;
            float wordPitch = 0;
            while (index < pieces.Count && !pieces[index].IsSpace)
            {
                wordPixels += pieces[index].Pixels;
                wordPitch = Math.Max(wordPitch, pieces[index].Pitch);
                index++;
            }

            if (!lineHasWord)
            {
                linePixels = wordPixels;
                linePitch = wordPitch;
                lineHasWord = true;
            }
            else if (CanonicalTextMeasurer.PixelsToPoints(linePixels + gapPixels + wordPixels) <= wrapWidth)
            {
                linePixels += gapPixels + wordPixels;
                linePitch = Math.Max(linePitch, Math.Max(gapPitch, wordPitch));
            }
            else
            {
                lines.Add(((float) CanonicalTextMeasurer.PixelsToPoints(linePixels), linePitch));
                linePixels = wordPixels;
                linePitch = wordPitch;
            }

            gapPixels = 0;
            gapPitch = 0;
        }

        if (lineHasWord)
        {
            lines.Add(((float) CanonicalTextMeasurer.PixelsToPoints(linePixels), linePitch));
        }

        if (lines.Count == 0)
        {
            // An empty paragraph still occupies one line at its mark's pitch.
            lines.Add((0, MarkPitch(paragraph)));
        }

        return lines;
    }

    List<Piece> Flatten(ParagraphElement paragraph)
    {
        var pieces = new List<Piece>();
        foreach (var run in paragraph.Runs)
        {
            if (run.IsTab || run.InlineImageData != null || run.Properties.Hidden || string.IsNullOrEmpty(run.Text))
            {
                continue;
            }

            var metrics = resolveFont(run.Properties.FontFamily, run.Properties.Bold, run.Properties.Italic);
            if (metrics is not { AdvanceWidths.Count: > 0 })
            {
                continue;
            }

            var size = run.Properties.FontSizePoints;
            var pitch = (float) metrics.LinePitchPoints(size);
            foreach (var (text, isSpace) in TokenizeText(run.Text))
            {
                pieces.Add(new(isSpace, CanonicalTextMeasurer.LinearPixels(metrics, text, size), pitch));
            }
        }

        return pieces;
    }

    float MarkPitch(ParagraphElement paragraph)
    {
        var mark = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Properties : new RunProperties();
        var metrics = resolveFont(mark.FontFamily, mark.Bold, mark.Italic);
        return metrics == null ? 0 : (float) metrics.LinePitchPoints(mark.FontSizePoints);
    }

    // Splits text into maximal runs of spaces vs non-spaces (U+0020 only — the inter-word break),
    // matching CanonicalTextMeasurer.WrapLines.
    static IEnumerable<(string Text, bool IsSpace)> TokenizeText(string text)
    {
        var start = 0;
        for (var i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || (text[i] == ' ') != (text[start] == ' '))
            {
                yield return (text[start..i], text[start] == ' ');
                start = i;
            }
        }
    }
}
