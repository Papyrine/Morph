/// <summary>
/// Backend-independent font metrics read straight from a font file's OpenType tables, so the
/// layout engine can measure line heights (and, later, glyph advances) without consulting
/// SkiaSharp / SixLabors.Fonts / PdfSharp. This is the single canonical metric source the layout
/// engine is built on — see <c>docs/layout-engine.md</c>. Divergent per-backend metrics
/// are the root cause of the page-count knife-edges (<c>src/page_counts.md</c>); reading the numbers
/// once, here, is how every backend is made to paginate identically.
///
/// <para>Raw values are in font design units; divide by <see cref="UnitsPerEm"/> and multiply by the
/// point size to convert to points.</para>
/// </summary>
sealed record FontMetrics
{
    /// <summary><c>head.unitsPerEm</c> — the design grid the other values are expressed in (commonly 2048 or 1000).</summary>
    public required int UnitsPerEm { get; init; }

    /// <summary><c>hhea.ascender</c> — distance the font rises above the baseline (positive).</summary>
    public required int Ascender { get; init; }

    /// <summary><c>hhea.descender</c> — distance the font drops below the baseline (negative in the font).</summary>
    public required int Descender { get; init; }

    /// <summary><c>hhea.lineGap</c> — the font's recommended extra leading between lines.</summary>
    public required int LineGap { get; init; }

    /// <summary>
    /// The single-spaced line box in design units — Word's rule, settled by two Word probes
    /// (<c>src/page_counts.md</c>, "Height model"): a font that sets USE_TYPO_METRICS takes the
    /// typographic box (<c>sTypoAscender − sTypoDescender + sTypoLineGap</c>); any other OS/2-bearing
    /// font takes the GDI cell — <c>usWinAscent + usWinDescent</c> plus the external leading
    /// <c>max(0, hheaGap − (winTotal − (hheaAsc − hheaDesc)))</c>. The original hhea-box model was
    /// XPS-validated only on fonts where all three coincide (Aptos's typo box and Calibri's win cell
    /// both equal their hhea boxes); Baskerville Old Face split them — hhea 1.0000 em, GDI 1.1406 em —
    /// and Word measured 14.64/17.76 pt at 13 pt single/×1.2, the GDI numbers, leaving business-plans/05
    /// compressed ~2.2 pt per line under the hhea model. Falls back to the hhea box for a font with no
    /// <c>OS/2</c> table.
    /// </summary>
    public int LineBoxUnits
    {
        get
        {
            if (UseTypoMetrics)
            {
                return TypoAscender - TypoDescender + TypoLineGap;
            }

            if (WinAscent > 0)
            {
                var winTotal = WinAscent + WinDescent;
                return winTotal + Math.Max(0, LineGap - (winTotal - (Ascender - Descender)));
            }

            return Ascender - Descender + LineGap;
        }
    }

    /// <summary>The single-spaced line pitch in points at <paramref name="sizePoints"/>.</summary>
    public double LinePitchPoints(double sizePoints) => (double) LineBoxUnits / UnitsPerEm * sizePoints;

    /// <summary>
    /// <c>OS/2.usWinAscent</c> — the ascent Windows lays text out against for a font that does not opt into
    /// typographic metrics. Zero when the font declares no <c>OS/2</c> table.
    /// </summary>
    public int WinAscent { get; init; }

    /// <summary><c>OS/2.usWinDescent</c> — the descent below the baseline Windows reserves (positive). Zero when the font declares no <c>OS/2</c> table.</summary>
    public int WinDescent { get; init; }

    /// <summary><c>OS/2.sTypoAscender</c> — the typographic ascent. Zero when the font declares no <c>OS/2</c> table.</summary>
    public int TypoAscender { get; init; }

    /// <summary><c>OS/2.sTypoDescender</c> — the typographic descent (negative in the font). Zero when the font declares no <c>OS/2</c> table.</summary>
    public int TypoDescender { get; init; }

    /// <summary><c>OS/2.sTypoLineGap</c> — the typographic line gap. Zero when the font declares no <c>OS/2</c> table.</summary>
    public int TypoLineGap { get; init; }

    /// <summary><c>OS/2.fsSelection</c> bit 7 (USE_TYPO_METRICS) — the font opts into typographic metrics.</summary>
    public bool UseTypoMetrics { get; init; }

    /// <summary>
    /// The strikethrough stroke (<c>OS/2</c> <c>yStrikeoutPosition</c> / <c>yStrikeoutSize</c>), in
    /// font units: how far the stroke's top sits above the baseline, and its thickness. Zero when the
    /// font declares no <c>OS/2</c> table. Word draws a footnote separator as exactly this stroke of the
    /// separator paragraph's font (<c>_probe_fn_l/_m/_o</c>, 2026-09-05).
    /// </summary>
    public int StrikeoutPosition { get; init; }

    public int StrikeoutSize { get; init; }

    /// <summary>
    /// The descent below the baseline that Word reserves at the bottom of the line box — the same
    /// family <see cref="LineBoxUnits"/> is built from: <c>−sTypoDescender</c> for a font that sets
    /// USE_TYPO_METRICS, <c>usWinDescent</c> for any other OS/2-bearing font, the hhea descender
    /// for a font with no <c>OS/2</c> table.
    /// </summary>
    public int DescentUnits
    {
        get
        {
            if (UseTypoMetrics)
            {
                return -TypoDescender;
            }

            return WinAscent > 0 ? WinDescent : -Descender;
        }
    }

    /// <summary>
    /// Where the baseline sits below the top of the line box: the box minus <see cref="DescentUnits"/>,
    /// so the line gap / external leading is stacked ABOVE the text and the descent is what remains
    /// below it. Word-measured 2026-09-04 (<c>_probe_baseline</c> / <c>_probe_baseline2</c>: 23 faces at
    /// 24/48/96pt, first baseline read from the XPS <c>Glyphs</c> origin against the page's top margin,
    /// both flagged and unflagged faces, static and variable). Every face lands here; the earlier
    /// <c>usWinAscent</c> rule only coincided on faces with no leading (Calibri, Segoe UI, Baskerville
    /// Old Face) and was 0.071 em low on Aptos, 0.033 em on Arial, 0.19 em on Gabriola.
    /// The two halves of the rule that the earlier reading got backwards:
    /// <list type="bullet">
    /// <item>A flagged font takes the TYPO box and descent — Aptos 48pt sits 75px down a 98px box
    /// (typo 0.939 em; usWinAscent would be 81px), Gabriola 1.384 em (typo ascender 0.684 plus its
    /// 0.7 em typo line gap on top; usWinAscent 1.191). Bahnschrift's 0.994 em is typo ascender + typo
    /// gap, which merely equals its usWinAscent.</item>
    /// <item>An unflagged font takes the GDI cell plus external leading, leading on top — Arial's 67-unit
    /// hhea gap raises its baseline from 0.905 em to 0.938 em; Times New Roman's 87 from 0.891 to 0.934.</item>
    /// </list>
    /// </summary>
    public int BaselineAscentUnits => LineBoxUnits - DescentUnits;

    /// <summary>
    /// The baseline's distance below the line-box top, in points at <paramref name="sizePoints"/>. The
    /// descent is a whole number of pixels on Word's 120-dpi layout grid: the probe's baselines are
    /// <c>round(box px) − round(descent px)</c> on 65 of 68 face/size rows, the other three one pixel
    /// off (Word rounds Verdana 96pt's 33.59px descent down); <c>round(ascent + gap)</c> is worse
    /// (10 misses, Calibri 48pt alone lands 77px against its 76.17). The box itself stays fractional
    /// here — its snap is the per-line accumulation the fragmenter deliberately does not reproduce
    /// (<c>docs/word-features.md</c>, Line Spacing) — so only the descent term is quantised.
    /// </summary>
    public double BaselineAscentPoints(double sizePoints)
    {
        var descentPixels = Math.Round((double) DescentUnits / UnitsPerEm * sizePoints * CanonicalTextMeasurer.ReferenceDpi / 72.0, MidpointRounding.AwayFromZero);
        return LinePitchPoints(sizePoints) - descentPixels * 72.0 / CanonicalTextMeasurer.ReferenceDpi;
    }

    /// <summary>
    /// Horizontal advance widths from <c>hmtx</c>, one per glyph up to <c>hhea.numberOfHMetrics</c>.
    /// Monospaced-tail glyphs past that index reuse the last entry (the OpenType convention for CJK
    /// fonts whose trailing glyphs share one advance). Empty when the font declares no <c>hmtx</c>.
    /// </summary>
    public IReadOnlyList<ushort> AdvanceWidths { get; init; } = [];

    /// <summary>
    /// Codepoint → glyph id, parsed from a Unicode <c>cmap</c> subtable (format 4 or 12). Unmapped
    /// codepoints resolve to glyph 0 (<c>.notdef</c>) via <see cref="AdvanceUnits"/>. Empty when the
    /// font declares no supported <c>cmap</c>.
    /// </summary>
    public IReadOnlyDictionary<int, ushort> GlyphForCodepoint { get; init; } = new Dictionary<int, ushort>();

    /// <summary>
    /// Word-measured advance overrides from a <c>.wordadvances</c> sidecar next to the font file,
    /// or null for a font without one. Word does not lay text out on the font's linear <c>hmtx</c>
    /// advances: it rounds the em to whole pixels on its 120-dpi layout grid per size and takes
    /// per-glyph GDI natural widths, most of which snap to whole pixels at text sizes — and the
    /// snap depends on the authored point size, not just the resulting pixel em (10.5pt and 11pt
    /// both render on an 18px em with different <c>n</c> advances). No public API reproduces the
    /// values (DirectWrite's GDI-compatible mode rounds cells Word keeps fractional), so the
    /// sidecar memoizes Word itself, measured from its XPS output: keyed by half-point size, then
    /// codepoint, value in pixels on the 120-dpi reference grid — the same grid
    /// <see cref="CanonicalTextMeasurer"/> accumulates in. Codepoints absent at a covered
    /// size, and sizes absent entirely, fall back to linear at the rounded em plus the measured
    /// half-twip bias: <c>design/upm * (round(pt*5/3) + 1/24)</c>.
    ///
    /// <para>Live since 2026-08-30 (the Calibri five) and 2026-09-06 (twenty faces). These are the
    /// COMPATIBILITY MODE 14-and-below values — Word's GDI-compatible whole-pixel glyph widths, which
    /// every settings-less package and 204 corpus documents take. A mode 15 document lays text out
    /// on DirectWrite's fractional widths instead, up to a pixel narrower per glyph (Segoe UI 12pt
    /// 's': a constant 9px in mode 12, 8/8/8/9 in mode 15 — <c>_probe_seg_m15</c>), so mode 15 reads
    /// <see cref="WordAdvancesMode15"/>; <see cref="WordAdvancesFor"/> picks. Measuring a mode 15
    /// document with the mode 12 table wrapped agendas-minutes/15 a line short per paragraph.</para>
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, float>>? WordAdvances { get; init; }

    /// <summary>
    /// The compatibility-mode-15 sidecar (<c>*.wordadvances15</c>, generated with a probe package
    /// declaring <c>compatibilityMode 15</c>): Word's fractional DirectWrite widths, in the same
    /// per-size, per-codepoint pixel form as <see cref="WordAdvances"/> — a hinted integer where the
    /// font grid-fits at that pixel size, fractional where it does not. Null when not generated, and
    /// the face measures linearly in mode 15. Shipped for the Calibri five; the tables generated for
    /// the other faces are held back until Word's mode-15 kerning (a kerned pair's first glyph floors)
    /// is modelled — see <c>docs/word-features.md</c>, Fonts.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, float>>? WordAdvancesMode15 { get; init; }

    /// <summary>The sidecar a document in the given compatibility mode measures with, or null.</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, float>>? WordAdvancesFor(int compatibilityMode) =>
        compatibilityMode >= 15 ? WordAdvancesMode15 : WordAdvances;

    /// <summary>
    /// GPOS <c>kern</c>-feature pair kerning, or null for a font without usable pair data. Word
    /// applies these adjustments when kerning is enabled for a run (<c>w:kern</c>; the built-in
    /// Normal of a document with no docDefaults kerns by default — <c>_probe_kern_*</c>), with the
    /// pair quantization implemented in <see cref="CanonicalTextMeasurer"/>.
    /// </summary>
    public GposKernTable? KernPairs { get; init; }

    /// <summary>The glyph id <paramref name="codepoint"/> maps to — glyph 0 (<c>.notdef</c>) when unmapped.</summary>
    public ushort GlyphId(int codepoint) => GlyphForCodepoint.GetValueOrDefault(codepoint, (ushort) 0);

    /// <summary>
    /// The horizontal advance of <paramref name="codepoint"/> in design units. Falls back to the last
    /// <c>hmtx</c> entry for glyphs past the metric count, and to glyph 0 for unmapped codepoints.
    /// Returns 0 when the font carries no advance data.
    /// </summary>
    public int AdvanceUnits(int codepoint)
    {
        if (AdvanceWidths.Count == 0)
        {
            return 0;
        }

        var glyph = GlyphForCodepoint.GetValueOrDefault(codepoint, (ushort) 0);
        return glyph < AdvanceWidths.Count ? AdvanceWidths[glyph] : AdvanceWidths[^1];
    }
}
