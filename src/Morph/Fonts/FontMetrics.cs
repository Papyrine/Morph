/// <summary>
/// Backend-independent font metrics read straight from a font file's OpenType tables, so the
/// layout engine can measure line heights (and, later, glyph advances) without consulting
/// SkiaSharp / SixLabors.Fonts / PdfSharp. This is the single canonical metric source the layout
/// engine is built on — see <c>docs/layout-engine-proposal.md</c>. Divergent per-backend metrics
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
    /// The single-spaced line box in design units: <c>ascender - descender + lineGap</c> (descender is
    /// negative, so this is ascent + |descent| + gap). This is the XPS-validated Word line pitch
    /// (<c>src/page_counts.md</c>, "Height model"): PdfSharp's <c>GetHeight()</c> and Skia's
    /// <c>ascent + descent + leading</c> both equal it for every bundled font.
    /// </summary>
    public int LineBoxUnits => Ascender - Descender + LineGap;

    /// <summary>The single-spaced line pitch in points at <paramref name="sizePoints"/>.</summary>
    public double LinePitchPoints(double sizePoints) => (double) LineBoxUnits / UnitsPerEm * sizePoints;

    /// <summary>
    /// <c>OS/2.usWinAscent</c> — the ascent Windows lays text out against for a font that does not opt into
    /// typographic metrics. Zero when the font declares no <c>OS/2</c> table.
    /// </summary>
    public int WinAscent { get; init; }

    /// <summary><c>OS/2.sTypoAscender</c> — the typographic ascent. Zero when the font declares no <c>OS/2</c> table.</summary>
    public int TypoAscender { get; init; }

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
    /// <c>OS/2</c> table. Deliberately independent of <see cref="LineBoxUnits"/>, which stays on the hhea
    /// box because that is what Word's XPS-measured line pitch matches.
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
