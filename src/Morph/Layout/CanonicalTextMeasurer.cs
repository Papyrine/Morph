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
    /// The <c>exactly</c> and <c>atLeast</c> splits are settled by the four-magnitude fixtures
    /// themselves (12/18/24/36pt against Word's references, band starts within 1px at every step):
    ///
    /// <list type="bullet">
    /// <item>EXACT hard-sets the baseline at 80% of the declared box, whatever the font's natural
    /// ascent — the same rule LibreOffice implements for Word compatibility
    /// (<c>itrform2.cxx</c>). Keeping the natural ascent left every taller-than-natural exact box
    /// with its ink riding high: <c>line_spacing_exactly</c>'s band gaps ran 42/54/67px against
    /// Word's 51/65/87, and the 0.8 rule predicts 51.7/64.2/86.7.</item>
    /// <item>AT-LEAST, once the declared box governs, anchors the ink at the BOTTOM: the extra
    /// space goes entirely above the text, so the ascent grows by the full box excess.
    /// <c>line_spacing_at_least</c>'s gaps predict 54.1/66.7/91.7px against Word's 55/66/92,
    /// where the natural-ascent model gave 47/55/66.</item>
    /// </list>
    /// </summary>
    public static double LineAscentPoints(
        double naturalAscentPoints,
        LineSpacingRule rule,
        double multiplier,
        double explicitPoints = 0,
        double naturalPitchPoints = 0)
    {
        if (rule == LineSpacingRule.Exactly && explicitPoints > 0)
        {
            return explicitPoints * 0.8;
        }

        if (rule == LineSpacingRule.AtLeast && explicitPoints > naturalPitchPoints && naturalPitchPoints > 0)
        {
            return naturalAscentPoints + (explicitPoints - naturalPitchPoints);
        }

        if (rule == LineSpacingRule.Auto && multiplier < 1)
        {
            return naturalAscentPoints * multiplier;
        }

        return naturalAscentPoints;
    }

    // The reference rasterizer runs at 120 dpi — the 125%-scaled display the XPS baselines were
    // measured on. It is the grid the pen position rounds onto; the em itself is not rounded (EmPixels).
    internal const double ReferenceDpi = 120.0;

    /// <summary>
    /// The device-pixel em size text lays out at, <c>sizePoints * 120/72</c> — deliberately NOT rounded.
    ///
    /// <para>This used to round to a whole pixel, which bucketed 10.5pt and 11pt onto the same 18px em
    /// and wrapped them identically. Measuring Word directly settled it (the probe is recorded in
    /// <c>src/page_counts.md</c>, "Ppem grain root-caused"): a run of one repeated glyph shows Word's
    /// advances landing on whole device pixels while their *mean* tracks the plain fractional advance,
    /// so the unrounded em is the model that fits. Rounding the em — onto a fixed 120-dpi
    /// grid unrelated to the output resolution, at that — made the width error jump ~4% between adjacent
    /// point sizes, and that discontinuity, not its magnitude, is what wrapped 10 / 10.5pt documents
    /// early while 11pt behaved. The quantization that remains is
    /// <see cref="PixelsToPoints"/> rounding the accumulated pen position once per line.</para>
    ///
    /// <para>Word DRAWS on a rounded em — its XPS output declares 7.8pt (13px) for 8pt Calibri, with
    /// whole-pixel glyph advances on that grid — but it does not LAY OUT on it. Probed 2026-10-02 by
    /// stepping a right indent one pixel at a time: a line's last word wraps exactly where the linear
    /// width (the <c>hmtx</c> advance at the size as authored, plus GPOS kerning scaled the same way)
    /// passes the measure, on 76 of 76 thresholds across Calibri, Arial, Aptos and Times New Roman at
    /// 10/11/12pt in compatibility modes 12 and 15, and an autofit column sizes to the same linear
    /// width (990 tables). The glyph positions in the XPS are presentation and say nothing about
    /// where a line breaks: per-glyph tables memoizing them (the <c>.wordadvances</c> sidecars this
    /// measurer read from 2026-08-30 until that probe) measured those sentences 1.6% under to 4.2%
    /// over. The numbers are in <c>docs/layout-engine.md</c>, "The crux".</para>
    /// </summary>
    public static double EmPixels(double sizePoints) =>
        sizePoints * ReferenceDpi / 72.0;

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
    /// <para>With <paramref name="kerning"/> the GPOS pair adjustments are added in the same design
    /// units and scaled with the advances, which is how Word's layout takes them. Probed 2026-10-02:
    /// the wrap threshold of a kerned sentence sits at <c>ceil</c> of exactly this width (Calibri and
    /// Aptos at three sizes each), and ten <c>To</c> set solid make autofit columns of 185.2, 169.2 and
    /// 154.2px at 12, 11 and 10pt Calibri where this gives 185.2, 169.7 and 154.3. The rule that stood
    /// here before snapped the kern to 1/16px and rounded the pair's first glyph to a whole pixel, which
    /// is how Word DRAWS a kerned pair (it was read off XPS glyph positions: 24pt <c>Ta</c> draws T at
    /// 17.000px from an unkerned 20.042). As a layout width it gave 185.5, 166.7 and 157.9 for those
    /// columns and put an Aptos line of 22 kerned pairs 4.3px narrow.</para>
    /// </summary>
    public static double LinearPixels(FontMetrics metrics, string text, double sizePoints, double fontWidthScale = 1.0, bool kerning = false)
    {
        var units = AdvanceUnits(metrics, text);
        if (kerning)
        {
            units += KernUnits(metrics, text);
        }

        return (double) units / metrics.UnitsPerEm * EmPixels(sizePoints) * fontWidthScale;
    }

    // The pair adjustments between each glyph of the text and the next, in design units.
    static long KernUnits(FontMetrics metrics, string text)
    {
        if (metrics.KernPairs is not { } kernTable)
        {
            return 0;
        }

        long units = 0;
        var previousGlyph = (ushort) 0;
        var havePrevious = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = metrics.GlyphId(rune.Value);
            if (havePrevious)
            {
                units += kernTable.KernUnits(previousGlyph, glyph);
            }

            previousGlyph = glyph;
            havePrevious = true;
        }

        return units;
    }

    /// <summary>
    /// The pair adjustment between the last glyph of <paramref name="before"/> and the first glyph of
    /// <paramref name="after"/>, in the same device pixels as <see cref="LinearPixels"/> — the kern
    /// that falls BETWEEN two pieces of text measured separately. Word's layout counts a pair across
    /// a space like any other: probed 2026-10-02 on autofit columns (<c>_probe_spacekern</c>), twenty
    /// <c>A</c> set a space apart make a 331.4px column in 12pt Arial, whose <c>A</c>+space and
    /// space+<c>A</c> pairs are −113 units each, against 372.4px with kerning off; Times New Roman and
    /// Avenir Next LT Pro agree. Zero when the font carries no pair for them.
    /// </summary>
    public static double KernPixelsBetween(FontMetrics metrics, string before, string after, double sizePoints, double fontWidthScale = 1.0)
    {
        if (metrics.KernPairs is not { } kernTable || before.Length == 0 || after.Length == 0)
        {
            return 0;
        }

        var last = default(Rune);
        foreach (var rune in before.EnumerateRunes())
        {
            last = rune;
        }

        var first = default(Rune);
        foreach (var rune in after.EnumerateRunes())
        {
            first = rune;
            break;
        }

        var units = kernTable.KernUnits(metrics.GlyphId(last.Value), metrics.GlyphId(first.Value));
        return (double) units / metrics.UnitsPerEm * EmPixels(sizePoints) * fontWidthScale;
    }

    /// <summary>
    /// The kern each glyph of <paramref name="text"/> carries, in points: at index <c>i</c> (a UTF-16
    /// index into the text) the sum of the pair adjustments between the glyphs before it, so a painter
    /// that draws at its backend's own advances adds it to each glyph's pen and the ink is kerned as
    /// the line was measured by <see cref="LinearPixels"/>. A backend that draws a run at unkerned
    /// advances draws it wider than it was measured, and the run after it on the line starts inside
    /// the ink — wedding/03's 30pt title lost 5px that way. Null when no pair of the text adjusts.
    /// </summary>
    public static double[]? KernShiftsPoints(FontMetrics metrics, string text, double sizePoints, double fontWidthScale = 1.0)
    {
        if (metrics.KernPairs is not { } kernTable || text.Length < 2)
        {
            return null;
        }

        var shifts = new double[text.Length];
        long units = 0;
        var any = false;
        var previousGlyph = (ushort) 0;
        var havePrevious = false;
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = metrics.GlyphId(rune.Value);
            if (havePrevious)
            {
                var pair = kernTable.KernUnits(previousGlyph, glyph);
                units += pair;
                any |= pair != 0;
            }

            shifts[index] = (double) units / metrics.UnitsPerEm * sizePoints * fontWidthScale;
            index += rune.Utf16SequenceLength;
            previousGlyph = glyph;
            havePrevious = true;
        }

        if (!any)
        {
            return null;
        }

        return shifts;
    }

    /// <summary>
    /// The end (exclusive) of the stretch of <paramref name="text"/> from <paramref name="start"/> whose
    /// glyphs all carry the kern of the glyph at <paramref name="start"/> in <paramref name="shifts"/>
    /// (from <see cref="KernShiftsPoints"/>), so a painter draws each stretch as one string at one pen and
    /// splits only where a pair adjusts. A surrogate pair is never split.
    /// </summary>
    public static int KernSegmentEnd(string text, double[] shifts, int start)
    {
        var shift = shifts[start];
        var end = start;
        while (end < text.Length)
        {
            var length = char.IsHighSurrogate(text[end]) && end + 1 < text.Length ? 2 : 1;
            if (end > start && shifts[end] != shift)
            {
                break;
            }

            end += length;
        }

        return end;
    }

    /// <summary>Quantizes an accumulated linear-pixel total to points — the pen position rounded once.</summary>
    public static double PixelsToPoints(double pixels) =>
        Math.Round(pixels, MidpointRounding.AwayFromZero) * 72.0 / ReferenceDpi;

    /// <summary>The reference device pixels a fixed point width occupies — the inverse of
    /// <see cref="PixelsToPoints"/>, for placing an unbreakable box (an inline image) on the pixel track.</summary>
    public static double PixelsFromPoints(double points) =>
        points * ReferenceDpi / 72.0;

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
    /// <paramref name="maxWidthPoints"/>, breaking at spaces, after a dash (see
    /// <see cref="SplitAfterDashes"/>) and at explicit <c>\n</c>. Returns one entry per line. A single
    /// word wider than the measure occupies its own line — Word overflows rather than splitting a word
    /// with no break opportunity in it.
    /// </summary>
    public static List<string> WrapLines(FontMetrics metrics, string text, double sizePoints, double maxWidthPoints)
    {
        var lines = new List<string>();
        var spacePixels = LinearPixels(metrics, " ", sizePoints);
        foreach (var segment in text.Split('\n'))
        {
            var current = new StringBuilder();
            double linePixels = 0;
            var afterSpace = false;
            foreach (var word in segment.Split(' '))
            {
                foreach (var chunk in SplitAfterDashes(word))
                {
                    var chunkPixels = LinearPixels(metrics, chunk, sizePoints);

                    // A hyphen break carries no space, so only a chunk that opens a space-delimited word
                    // pays the gap — this is what keeps "E2E-" and "FinalisedActions-" adjacent.
                    var gapPixels = afterSpace ? spacePixels : 0;
                    if (current.Length == 0)
                    {
                        current.Append(chunk);
                        linePixels = chunkPixels;
                    }
                    // Measure the whole candidate line (its cumulative pixels, rounded once) so the pen
                    // position tracks the linear ideal instead of accumulating a per-word rounding error.
                    else if (PixelsToPoints(linePixels + gapPixels + chunkPixels) <= maxWidthPoints)
                    {
                        if (afterSpace)
                        {
                            current.Append(' ');
                        }

                        current.Append(chunk);
                        linePixels += gapPixels + chunkPixels;
                    }
                    else
                    {
                        lines.Add(current.ToString());
                        current.Clear().Append(chunk);
                        linePixels = chunkPixels;
                    }

                    afterSpace = false;
                }

                afterSpace = true;
            }

            lines.Add(current.ToString());
        }

        return lines;
    }

    /// <summary>
    /// Splits a word after each run of dashes, so "E2E-FinalisedActions-44b577b2" yields "E2E-",
    /// "FinalisedActions-", "44b577b2". A word holding no dash yields itself.
    /// </summary>
    /// <remarks>
    /// Word treats a dash as a line-break opportunity and the break falls AFTER it, so the dash stays on
    /// the upper line. Probed by squeezing a token in an autofit table's first column against a second
    /// column long enough to take every spare point, and comparing that width against the same token's
    /// natural single-line width (<c>_probe_hyphen</c>, 10 cases, natural → squeezed in points):
    /// <list type="bullet">
    /// <item>breaks — <c>well-known-example-string</c> 128.05 → 78.95, <c>2024-2025-2026-2027</c>
    /// 104.55 → 61.45, en dash U+2013 119.45 → 83.30, em dash U+2014 132.95 → 88.00</item>
    /// <item>does not — a solid 29-character word (128.10, unmoved), a slash-separated one (143.35), and
    /// the NON-BREAKING hyphen U+2011 (116.85), which is the control proving the effect is the character
    /// and not the length</item>
    /// </list>
    /// Two edges settle where the break sits. A LEADING dash breaks: <c>-Supercalifragilisticexpial</c>
    /// went 118.10 → 115.70, and that 2.40pt is exactly the hyphen's own advance — Word put the lone dash
    /// on the upper line and the rest below, so no leading exemption belongs here. A TRAILING dash needs no
    /// exemption either: breaking after it leaves the whole word above and nothing below, which measures as
    /// unbreakable (118.10, unmoved) on its own.
    /// <para>
    /// A RUN of dashes breaks only after its last character, so "A--B" yields "A--" and "B" rather than
    /// stranding a dash at the head of a line. The probe cannot separate that from breaking after the
    /// first (both give the same minimum), so it is chosen for the rendering rather than measured.
    /// </para>
    /// </remarks>
    public static IEnumerable<string> SplitAfterDashes(string word)
    {
        var start = 0;
        for (var i = 0; i < word.Length - 1; i++)
        {
            if (IsDash(word[i]) && !IsDash(word[i + 1]))
            {
                yield return word[start..(i + 1)];
                start = i + 1;
            }
        }

        yield return word[start..];
    }

    // U+2011 NON-BREAKING HYPHEN is deliberately absent — that is the whole point of the character, and
    // the probe confirms Word does not break at one.
    static bool IsDash(char character) =>
        character is '-' or '–' or '—';
}
