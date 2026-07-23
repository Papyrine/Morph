namespace Morph;

/// <summary>
/// Options for the vector-text PDF exporter.
/// </summary>
public sealed record PdfExportOptions : ExportOptions
{
    /// <summary>
    /// Scale factor for font width measurements. Values &gt; 1.0 make text wider (earlier line
    /// wrapping); 1.08 tracks Microsoft Word's slightly looser GDI metrics. The default is 1.0,
    /// which leaves layout unchanged.
    /// <para>
    /// Applies to PDF text (wrap points and right/decimal tab stops) and to WordArt rasterized
    /// through a raster backend (see <see cref="RasterizeWordArt"/>). For output consistent with a
    /// raster export of the same document, use the same value here as on the image options.
    /// </para>
    /// </summary>
    public double FontWidthScale { get; init; } = DefaultFontSettings.FontWidthScale;

    /// <summary>
    /// Optional delegate to resolve missing fonts. Called with the font family name that could not
    /// be found; return an alternative family, or null to fall through to the curated alias map,
    /// the platform resolver, and finally <see cref="ExportOptions.DefaultFont"/>.
    /// <para>
    /// Consulted only once the <see cref="ExportOptions.FontDirectory"/> / bundled faces and the
    /// host's installed fonts have both missed, so a family the machine can already serve never
    /// reaches it.
    /// </para>
    /// </summary>
    public Func<string, string?>? FontFallback { get; init; }

    /// <summary>
    /// When set, only pages within this 1-based inclusive range are rendered. Null (default)
    /// renders every page.
    /// </summary>
    public PageRange? Pages { get; init; }

    /// <summary>
    /// When true (the default), inline and floating WordArt is rendered with full fidelity
    /// (glyph warps, outline, shadow, glow, reflection) by rasterizing it through a raster backend
    /// — <c>Morph.Skia</c> if it can be loaded, otherwise <c>Morph.ImageSharp</c> — and embedding
    /// the resulting image. This takes effect only when one of those assemblies is deployed
    /// alongside <c>Morph.Pdf</c>; otherwise, and when set to false, WordArt falls back to plain
    /// text occupying the shape's box.
    /// </summary>
    public bool RasterizeWordArt { get; init; } = true;
}
