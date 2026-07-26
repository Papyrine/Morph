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
}
