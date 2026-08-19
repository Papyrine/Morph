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

    /// <summary>
    /// Where the baseline sits inside a <c>lineRule="auto"</c> (multiple) line box. The box itself is
    /// always the natural pitch times the multiple — <see cref="LineHeightPoints(double,LineSpacingRule,double,double)"/>
    /// — but the two directions divide it differently, and Word-probed
    /// (<c>_probe_linemultiple</c>: 48pt Aptos at multiples 0.6/0.7/0.8/0.9/1.0/1.158/1.25/1.5,
    /// baselines read from the XPS glyph origins):
    ///
    /// <list type="bullet">
    /// <item>EXPANDING (multiple &gt; 1) leaves the ascent alone and puts every extra point BELOW the
    /// baseline — measured ascents 45.1 / 44.5 / 45.1 against a natural 44.5, while the descent runs
    /// 13.8 → 22.8 → 28.2 → 42.6.</item>
    /// <item>COMPRESSING (multiple &lt; 1) scales the whole box, ascent included — measured 26.5 / 30.7 /
    /// 36.1 / 40.3 against 44.5 × the multiple = 26.7 / 31.2 / 35.6 / 40.1.</item>
    /// </list>
    ///
    /// Keeping the natural ascent in both directions — which is what this did until the probe — leaves
    /// compressed text sitting too low in its own box by <c>ascent × (1 − multiple)</c>: 9.2pt on
    /// business-plans/13's 0.8× title, and exactly the 19px its cover title measured low.
    ///
    /// Only <c>auto</c> is covered. Word's split under <c>exactly</c> and <c>atLeast</c> is a separate
    /// question this fixture does not ask, so those keep the natural ascent.
    /// </summary>
    public static double LineAscentPoints(double naturalAscentPoints, LineSpacingRule rule, double multiplier) =>
        rule == LineSpacingRule.Auto && multiplier < 1
            ? naturalAscentPoints * multiplier
            : naturalAscentPoints;

    // The reference rasterizer runs at 120 dpi — the 125%-scaled display the XPS baselines were
    // measured on. It is the grid the pen position rounds onto; the em itself is not rounded (EmPixels).
    const double referenceDpi = 120.0;

    /// <summary>
    /// The device-pixel em size text lays out at, <c>sizePoints * 120/72</c> — deliberately NOT rounded
    /// on the linear (no-sidecar) track.
    ///
    /// <para>This used to round to a whole pixel, which bucketed 10.5pt and 11pt onto the same 18px em
    /// and wrapped them identically. Measuring Word directly settled it (the probe is recorded in
    /// <c>src/page_counts.md</c>, "Ppem grain root-caused"): a run of one repeated glyph shows Word's
    /// advances landing on whole device pixels while their *mean* tracks the plain fractional advance,
    /// so on this track the unrounded em is the model that fits. Rounding the em — onto a fixed 120-dpi
    /// grid unrelated to the output resolution, at that — made the width error jump ~4% between adjacent
    /// point sizes, and that discontinuity, not its magnitude, is what wrapped 10 / 10.5pt documents
    /// early while 11pt behaved. The quantization that remains, and that Word does share, is
    /// <see cref="PixelsToPoints"/> rounding the accumulated pen position once per line.</para>
    ///
    /// <para>Word itself DOES round the em — its XPS output declares 7.8pt (13px) for 8pt Calibri —
    /// and takes per-glyph GDI natural widths on that grid, so per-glyph truth deviates from ANY
    /// single linear track, this one included. The unrounded em is kept here as the model that
    /// measured best corpus-wide for fonts without measured data (the discontinuity above was a real
    /// regression; the per-size deviations are not linearly correctable). Where the deviation is
    /// modelled, it is carried per glyph by <see cref="FontMetrics.WordAdvances"/> sidecars, which
    /// bypass this em entirely.</para>
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
    ///
    /// <para>A font carrying a <see cref="FontMetrics.WordAdvances"/> sidecar advances by Word's own
    /// measured per-glyph values instead of the linear <c>hmtx</c> track — see that property for the
    /// model and its evidence. The reference grid here is 120 dpi, the same grid Word's measured
    /// pixels are on, so sidecar values add into this accumulator directly.</para>
    /// </summary>
    public static double LinearPixels(FontMetrics metrics, string text, double sizePoints, double fontWidthScale = 1.0, bool kerning = false)
    {
        if (metrics.WordAdvances is { } wordAdvances)
        {
            return WordPixels(metrics, wordAdvances, text, sizePoints, kerning) * fontWidthScale;
        }

        if (kerning && metrics.KernPairs != null)
        {
            return KernedLinearPixels(metrics, text, sizePoints) * fontWidthScale;
        }

        return (double) AdvanceUnits(metrics, text) / metrics.UnitsPerEm * EmPixels(sizePoints) * fontWidthScale;
    }

    // Word's kerned-pair quantization, measured on the _probe_kern_* fixtures across three sizes
    // and six Calibri pairs (todo #43): the kern value snaps to 1/16 px at the layout em, and the
    // pair's FIRST glyph advance then rounds to a whole layout pixel — even where its unkerned
    // advance was fractional (24pt Ta renders T at 17.000px from an unkerned 20.042). Returns the
    // signed pixel delta to add for the pair, replacing the first glyph's unkerned advance with
    // Word's kerned one.
    static double KernPairDelta(double firstAdvancePixels, short kernUnits, double emPixels, int unitsPerEm)
    {
        var kernSixteenths = Math.Round((double) kernUnits / unitsPerEm * emPixels * 16, MidpointRounding.AwayFromZero) / 16;
        return Math.Round(firstAdvancePixels + kernSixteenths, MidpointRounding.AwayFromZero) - firstAdvancePixels;
    }

    // Kerning on the linear (no-sidecar) track: same pair rule, on the unrounded reference em.
    static double KernedLinearPixels(FontMetrics metrics, string text, double sizePoints)
    {
        var kernTable = metrics.KernPairs!;
        var emPixels = EmPixels(sizePoints);
        double pixels = 0;
        var previousGlyph = (ushort) 0;
        double previousAdvance = 0;
        var havePrevious = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = metrics.GlyphId(rune.Value);
            if (havePrevious)
            {
                var kern = kernTable.KernUnits(previousGlyph, glyph);
                if (kern != 0)
                {
                    pixels += KernPairDelta(previousAdvance, kern, emPixels, metrics.UnitsPerEm);
                }
            }

            var advance = (double) metrics.AdvanceUnits(rune.Value) / metrics.UnitsPerEm * emPixels;
            pixels += advance;
            previousGlyph = glyph;
            previousAdvance = advance;
            havePrevious = true;
        }

        return pixels;
    }

    // Word's measured advance track: per-glyph sidecar pixels where measured, else linear at the
    // em rounded to whole reference pixels plus Word's half-twip bias (FontMetrics.WordAdvances).
    // Kerning, when enabled, applies Word's pair rule (KernPairDelta) on the same em.
    static double WordPixels(FontMetrics metrics, IReadOnlyDictionary<int, IReadOnlyDictionary<int, float>> wordAdvances, string text, double sizePoints, bool kerning)
    {
        var halfPoints = (int) Math.Round(sizePoints * 2, MidpointRounding.AwayFromZero);
        wordAdvances.TryGetValue(halfPoints, out var table);
        var roundedEmPixels = Math.Round(EmPixels(sizePoints), MidpointRounding.AwayFromZero) + 1.0 / 24;
        var kernTable = kerning ? metrics.KernPairs : null;
        double pixels = 0;
        var previousGlyph = (ushort) 0;
        double previousAdvance = 0;
        var havePrevious = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (kernTable != null)
            {
                var glyph = metrics.GlyphId(rune.Value);
                if (havePrevious)
                {
                    var kern = kernTable.KernUnits(previousGlyph, glyph);
                    if (kern != 0)
                    {
                        pixels += KernPairDelta(previousAdvance, kern, roundedEmPixels, metrics.UnitsPerEm);
                    }
                }

                previousGlyph = glyph;
                havePrevious = true;
            }

            double advance;
            if (table != null && table.TryGetValue(rune.Value, out var measured))
            {
                advance = measured;
            }
            else
            {
                advance = (double) metrics.AdvanceUnits(rune.Value) / metrics.UnitsPerEm * roundedEmPixels;
            }

            pixels += advance;
            previousAdvance = advance;
        }

        return pixels;
    }

    /// <summary>Quantizes an accumulated linear-pixel total to points — the pen position rounded once.</summary>
    public static double PixelsToPoints(double pixels) =>
        Math.Round(pixels, MidpointRounding.AwayFromZero) * 72.0 / referenceDpi;

    /// <summary>The reference device pixels a fixed point width occupies — the inverse of
    /// <see cref="PixelsToPoints"/>, for placing an unbreakable box (an inline image) on the pixel track.</summary>
    public static double PixelsFromPoints(double points) =>
        points * referenceDpi / 72.0;

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
    public static double MeasureWidthPoints(FontMetrics metrics, string text, double sizePoints, double fontWidthScale = 1.0, bool kerning = false) =>
        PixelsToPoints(LinearPixels(metrics, text, sizePoints, fontWidthScale, kerning));

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
        var spacePixels = LinearPixels(metrics, " ", sizePoints);
        foreach (var segment in text.Split('\n'))
        {
            var current = new StringBuilder();
            double linePixels = 0;
            foreach (var word in segment.Split(' '))
            {
                var wordPixels = LinearPixels(metrics, word, sizePoints);
                if (current.Length == 0)
                {
                    current.Append(word);
                    linePixels = wordPixels;
                }
                // Measure the whole candidate line (its cumulative pixels, rounded once) so the pen
                // position tracks the linear ideal instead of accumulating a per-word rounding error.
                else if (PixelsToPoints(linePixels + spacePixels + wordPixels) <= maxWidthPoints)
                {
                    current.Append(' ').Append(word);
                    linePixels += spacePixels + wordPixels;
                }
                else
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                    linePixels = wordPixels;
                }
            }

            lines.Add(current.ToString());
        }

        return lines;
    }
}
