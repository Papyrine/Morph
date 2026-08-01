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
        double explicitPoints = 0) =>
        LineHeightPoints(metrics.LinePitchPoints(sizePoints), rule, multiplier, explicitPoints);

    /// <summary>
    /// Applies Word's line-spacing rule to an already-computed single-spaced pitch — used when a line
    /// mixes fonts and its pitch is the largest of its runs' hhea boxes rather than one font's pitch.
    /// </summary>
    public static double LineHeightPoints(
        double singleSpacedPitchPoints,
        LineSpacingRule rule = LineSpacingRule.Auto,
        double multiplier = 1.0,
        double explicitPoints = 0) =>
        rule switch
        {
            LineSpacingRule.Exactly => explicitPoints,
            LineSpacingRule.AtLeast => Math.Max(singleSpacedPitchPoints, explicitPoints),
            _ => singleSpacedPitchPoints * multiplier
        };

    // The reference rasterizer runs at 120 dpi — the 125%-scaled display the XPS baselines were
    // measured on — so text lays out at an integer ppem of round(size * 120/72) device pixels.
    const double referenceDpi = 120.0;

    /// <summary>
    /// The device-pixel em size text lays out at: <c>round(sizePoints * 120/72)</c>. 11pt and 10.5pt
    /// both round to 18px (em 10.8pt), so the measurer wraps them identically — the advance model in
    /// <c>src/page_counts.md</c>. A Word probe (recorded there) shows Word does not: it renders
    /// 10.5pt ~1.7% narrower than 11pt, so this bucketing over-widths 10 / 10.5pt text by ~2–3% and
    /// wraps it early. Word sits between this grain and raw-fractional (which over-corrects), matching
    /// no single-dpi integer ppem — this is the closest available approximation, not Word's model.
    /// </summary>
    public static int Ppem(double sizePoints) =>
        (int) Math.Round(sizePoints * referenceDpi / 72.0, MidpointRounding.AwayFromZero);

    static long AdvanceUnits(FontMetrics metrics, string text)
    {
        long units = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            units += metrics.AdvanceUnits(rune.Value);
        }

        return units;
    }

    /// <summary>
    /// The device pixels (unrounded) that <paramref name="text"/> advances at the reference ppem. This
    /// is the accumulator for pen-position rounding: summing it across runs of different fonts/sizes on
    /// one line and quantizing once with <see cref="PixelsToPoints"/> keeps a mixed-font line on the
    /// linear track, exactly as a single-font line stays on it. <paramref name="fontWidthScale"/> is the
    /// per-conversion widening (<c>PdfExportOptions</c>/<c>ImageExportOptions.FontWidthScale</c>), applied
    /// linearly before quantization — the same knob production's <c>RenderContextBase</c> multiplies advances by.
    /// </summary>
    public static double LinearPixels(FontMetrics metrics, string text, double sizePoints, double fontWidthScale = 1.0) =>
        (double) AdvanceUnits(metrics, text) / metrics.UnitsPerEm * Ppem(sizePoints) * fontWidthScale;

    /// <summary>Quantizes an accumulated linear-pixel total to points — the pen position rounded once.</summary>
    public static double PixelsToPoints(double pixels) =>
        Math.Round(pixels, MidpointRounding.AwayFromZero) * 72.0 / referenceDpi;

    /// <summary>The reference device pixels a fixed point width occupies — the inverse of
    /// <see cref="PixelsToPoints"/>, for placing an unbreakable box (an inline image) on the pixel track.</summary>
    public static double PixelsFromPoints(double points) =>
        points * referenceDpi / 72.0;

    // Converts a run of design units to points under pen-position rounding.
    static double PointsFromUnits(FontMetrics metrics, long units, int ppem) =>
        PixelsToPoints((double) units / metrics.UnitsPerEm * ppem);

    /// <summary>
    /// The unrounded advance width of <paramref name="text"/> in points: <c>Σ advanceUnits * size /
    /// unitsPerEm</c>. Used to check the <c>cmap</c>/<c>hmtx</c> pipeline against an independent reader;
    /// the wrap-driving measurement is the pixel-quantized <see cref="MeasureWidthPoints"/>.
    /// </summary>
    public static double MeasureWidthRawPoints(FontMetrics metrics, string text, double sizePoints) =>
        (double) AdvanceUnits(metrics, text) / metrics.UnitsPerEm * sizePoints;

    /// <summary>
    /// The advance width of <paramref name="text"/> in points that drives line breaking, matching
    /// Word's GDI/DirectWrite layout. The pen advances along the design-unit total, and the drawn
    /// position quantizes to an integer device pixel at the reference ppem — so the LINE total tracks
    /// the nominal-linear ideal to within half a pixel (<c>src/page_counts.md</c>, advance model),
    /// which is exactly the "inter-word spaces are elastic upward" behaviour: the flex is spread
    /// across the run rather than snapped per glyph. Rounding each glyph independently instead would
    /// accumulate upward and over-wrap long lines. A per-font upward factor (Aptos 1.0125×, Times New
    /// Roman 1.0213×, most others ≈ 1) is the last fraction of a percent and is not yet modelled.
    /// </summary>
    public static double MeasureWidthPoints(FontMetrics metrics, string text, double sizePoints, double fontWidthScale = 1.0) =>
        PixelsToPoints(LinearPixels(metrics, text, sizePoints, fontWidthScale));

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
        var ppem = Ppem(sizePoints);
        var spaceUnits = metrics.AdvanceUnits(' ');
        foreach (var segment in text.Split('\n'))
        {
            var current = new StringBuilder();
            long lineUnits = 0;
            foreach (var word in segment.Split(' '))
            {
                var wordUnits = AdvanceUnits(metrics, word);
                if (current.Length == 0)
                {
                    current.Append(word);
                    lineUnits = wordUnits;
                }
                // Measure the whole candidate line (its cumulative units, rounded once) so the pen
                // position tracks the linear ideal instead of accumulating a per-word rounding error.
                else if (PointsFromUnits(metrics, lineUnits + spaceUnits + wordUnits, ppem) <= maxWidthPoints)
                {
                    current.Append(' ').Append(word);
                    lineUnits += spaceUnits + wordUnits;
                }
                else
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                    lineUnits = wordUnits;
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }
}
