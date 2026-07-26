/// <summary>
/// The single canonical text measurer for the layout engine (<c>docs/layout-engine-proposal.md</c>):
/// given a font's OpenType <see cref="FontMetrics"/>, it computes layout measurements with no backend
/// font library involved, so every backend paginates from identical numbers rather than from
/// SkiaSharp / SixLabors.Fonts / PdfSharp metrics that diverge (the root cause of the page-count
/// knife-edges in <c>src/page_counts.md</c>).
///
/// <para>This is the growth point for step 1 of the migration. Today it owns line height — validated
/// against Word's XPS-measured pitch. Glyph-advance measurement and line breaking attach here next,
/// on top of the advance tables the <see cref="FontMetricsReader"/> will surface.</para>
/// </summary>
sealed class CanonicalTextMeasurer
{
    /// <summary>
    /// The laid-out height of one line at <paramref name="sizePoints"/> under Word's line-spacing rule.
    /// <see cref="LineSpacingRule.Auto"/> multiplies the single-spaced hhea pitch;
    /// <see cref="LineSpacingRule.Exactly"/> forces the value; <see cref="LineSpacingRule.AtLeast"/>
    /// takes the larger of the pitch and the value — mirroring the raster and PDF
    /// <c>CalculateLineHeight</c>, but computed from the canonical <see cref="FontMetrics"/> rather than
    /// a backend font object.
    /// </summary>
    public static double LineHeightPoints(
        FontMetrics metrics,
        double sizePoints,
        LineSpacingRule rule = LineSpacingRule.Auto,
        double multiplier = 1.0,
        double explicitPoints = 0)
    {
        var single = metrics.LinePitchPoints(sizePoints);
        return rule switch
        {
            LineSpacingRule.Exactly => explicitPoints,
            LineSpacingRule.AtLeast => Math.Max(single, explicitPoints),
            _ => single * multiplier
        };
    }

    // The reference rasterizer runs at 120 dpi — the 125%-scaled display the XPS baselines were
    // measured on — so text lays out at an integer ppem of round(size * 120/72) device pixels.
    const double referenceDpi = 120.0;

    /// <summary>
    /// The device-pixel em size text lays out at: <c>round(sizePoints * 120/72)</c>. 11pt and 10.5pt
    /// both round to 18px (em 10.8pt), which is why they wrap identically — the advance model in
    /// <c>src/page_counts.md</c>.
    /// </summary>
    public static int Ppem(double sizePoints) =>
        (int) Math.Round(sizePoints * referenceDpi / 72.0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The unrounded advance width of <paramref name="text"/> in points: <c>Σ advanceUnits * size /
    /// unitsPerEm</c>. Used to check the <c>cmap</c>/<c>hmtx</c> pipeline against an independent reader;
    /// the wrap-driving measurement is the pixel-quantized <see cref="MeasureWidthPoints"/>.
    /// </summary>
    public static double MeasureWidthRawPoints(FontMetrics metrics, string text, double sizePoints)
    {
        long units = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            units += metrics.AdvanceUnits(rune.Value);
        }

        return (double) units / metrics.UnitsPerEm * sizePoints;
    }

    /// <summary>
    /// The pixel-quantized advance width of <paramref name="text"/> in points that drives line
    /// breaking: each glyph advances by an integer number of device pixels at the reference ppem,
    /// matching Word's GDI/DirectWrite layout. Per-font inter-word-space elasticity — the last ~1% of
    /// the advance model — is a refinement layered on top of this base.
    /// </summary>
    public static double MeasureWidthPoints(FontMetrics metrics, string text, double sizePoints)
    {
        var ppem = Ppem(sizePoints);
        long pixels = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            pixels += (long) Math.Round((double) metrics.AdvanceUnits(rune.Value) / metrics.UnitsPerEm * ppem, MidpointRounding.AwayFromZero);
        }

        return pixels * 72.0 / referenceDpi;
    }

    /// <summary>
    /// Greedy word wrap: breaks <paramref name="text"/> into lines that each fit within
    /// <paramref name="maxWidthPoints"/>, breaking only at spaces (and at explicit <c>\n</c>). Returns
    /// one entry per line. A single word wider than the measure occupies its own line — Word overflows
    /// rather than splitting a word with no hyphenation point. Trailing-whitespace and hyphenation
    /// nuances are refinements for later; this is the base greedy break.
    /// </summary>
    public static List<string> WrapLines(FontMetrics metrics, string text, double sizePoints, double maxWidthPoints)
    {
        var lines = new List<string>();
        var spaceWidth = MeasureWidthPoints(metrics, " ", sizePoints);
        foreach (var segment in text.Split('\n'))
        {
            var current = new StringBuilder();
            var currentWidth = 0.0;
            foreach (var word in segment.Split(' '))
            {
                var wordWidth = MeasureWidthPoints(metrics, word, sizePoints);
                if (current.Length == 0)
                {
                    current.Append(word);
                    currentWidth = wordWidth;
                }
                else if (currentWidth + spaceWidth + wordWidth <= maxWidthPoints)
                {
                    current.Append(' ').Append(word);
                    currentWidth += spaceWidth + wordWidth;
                }
                else
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                    currentWidth = wordWidth;
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }
}
