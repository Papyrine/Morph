/// <summary>
/// The single canonical text measurer for the layout engine (<c>docs/layout-engine.md</c>):
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
    // measured on. It is the grid the pen position rounds onto; the em itself is not rounded (EmPixels).
    const double referenceDpi = 120.0;

    /// <summary>
    /// The device-pixel em size text lays out at, <c>sizePoints * 120/72</c> — deliberately NOT rounded.
    ///
    /// <para>This used to round to a whole pixel, which bucketed 10.5pt and 11pt onto the same 18px em
    /// and wrapped them identically. Measuring Word directly settled it (the probe is recorded in
    /// <c>src/page_counts.md</c>, "Ppem grain root-caused"): a run of one repeated glyph shows Word's
    /// advances landing on whole device pixels while their *mean* tracks the plain fractional advance,
    /// so Word rounds the pen position and never the em. Rounding the em — onto a fixed 120-dpi grid
    /// unrelated to the output resolution, at that — made the width error jump ~4% between adjacent
    /// point sizes, and that discontinuity, not its magnitude, is what wrapped 10 / 10.5pt documents
    /// early while 11pt behaved. The quantization that remains, and that Word does share, is
    /// <see cref="PixelsToPoints"/> rounding the accumulated pen position once per line.</para>
    /// </summary>
    public static double EmPixels(double sizePoints) =>
        sizePoints * referenceDpi / 72.0;

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
        (double) AdvanceUnits(metrics, text) / metrics.UnitsPerEm * EmPixels(sizePoints) * fontWidthScale;

    /// <summary>Quantizes an accumulated linear-pixel total to points — the pen position rounded once.</summary>
    public static double PixelsToPoints(double pixels) =>
        Math.Round(pixels, MidpointRounding.AwayFromZero) * 72.0 / referenceDpi;

    /// <summary>The reference device pixels a fixed point width occupies — the inverse of
    /// <see cref="PixelsToPoints"/>, for placing an unbreakable box (an inline image) on the pixel track.</summary>
    public static double PixelsFromPoints(double points) =>
        points * referenceDpi / 72.0;

    // Converts a run of design units to points under pen-position rounding.
    static double PointsFromUnits(FontMetrics metrics, long units, double emPixels) =>
        PixelsToPoints((double) units / metrics.UnitsPerEm * emPixels);

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
    /// Roman 1.0213×, most others ≈ 1) was measured and ruled out empirically — a wash applied to
    /// spaces only, a regression applied whole-advance (<c>src/page_counts.md</c>) — so it stays
    /// unmodelled by choice.
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
        var emPixels = EmPixels(sizePoints);
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
                else if (PointsFromUnits(metrics, lineUnits + spaceUnits + wordUnits, emPixels) <= maxWidthPoints)
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
