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
    /// Where the baseline sits below the top of the line box: <see cref="WinAscent"/>, for every font —
    /// including one that sets <c>USE_TYPO_METRICS</c> (OS/2 fsSelection bit 7). That flag is deliberately
    /// ignored, and the choice is measured, twice over. Word lays text out with GDI metrics, which use
    /// usWinAscent regardless of the flag; SkiaSharp honours the flag, so the production backends sit
    /// ~0.071 em high against Word on every flagged font (Aptos — the corpus default — Bahnschrift, Trade
    /// Gothic Next). A flag-honouring rule was implemented and measured corpus-wide: it matched the
    /// production renderers exactly (184 faces, zero mismatches against SkiaSharp) and moved the corpus
    /// AWAY from Word by −0.0017, 90 documents regressing — so it was reverted in favour of Word's own
    /// behaviour. The engine's baselines therefore intentionally diverge from the production renderers on
    /// flagged fonts, and beat them against Word there. "Match production" is not even one target:
    /// SkiaSharp's reported ascent is platform-dependent — usWinAscent on Windows (GDI-compatible), the
    /// hhea ascender on linux (FreeType), which is what the container-rendered baselines were drawn with —
    /// so this rule is anchored to Word, the one stable oracle. Falls back to the hhea ascender for a font with no
    /// <c>OS/2</c> table. Deliberately independent of <see cref="LineBoxUnits"/>: the baseline ignores the
    /// typo flag while the line pitch honours it — each side separately Word-probed.
    /// </summary>
    public int BaselineAscentUnits => WinAscent > 0 ? WinAscent : Ascender;

    /// <summary>The baseline's distance below the line-box top, in points at <paramref name="sizePoints"/>.</summary>
    public double BaselineAscentPoints(double sizePoints) => (double) BaselineAscentUnits / UnitsPerEm * sizePoints;

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
    /// <para>The generated Calibri sidecars currently ship PARKED as
    /// <c>src/Fonts/*.wordadvances.pending</c>: activating them without kerning measured worse
    /// against Word than the linear track, whose −2.4% narrowness had been cancelling the missing
    /// kerning (Word kerns Calibri; a title-line check put the sidecar model +0.35% of Word where
    /// the linear track sat −2.26%, and the residual is the kern pairs). Rename to
    /// <c>.wordadvances</c> to activate once kerning lands — <c>src/todo.md</c> #43.</para>
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, float>>? WordAdvances { get; init; }

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
