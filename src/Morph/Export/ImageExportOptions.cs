namespace Morph;

/// <summary>
/// Options for the raster (PNG) exporters. Replaces the older <c>ConversionOptions</c> type.
/// </summary>
public sealed record ImageExportOptions : ExportOptions
{
    /// <summary>Image resolution in dots per inch. Default is 150.</summary>
    public int Dpi { get; init; } = 150;

    /// <summary>
    /// Scale factor for font width measurements. Values &gt; 1.0 make text wider (earlier line
    /// wrapping). The default matches Microsoft Word's text rendering.
    /// </summary>
    public double FontWidthScale { get; init; } = DefaultFontSettings.FontWidthScale;

    /// <summary>
    /// Optional delegate to resolve missing fonts. Called with the font family name that could not
    /// be found; return an alternative family or null to fall back through the resolver chain.
    /// </summary>
    public Func<string, string?>? FontFallback { get; init; }

    /// <summary>
    /// Overrides <see cref="DefaultFontSettings.DeterministicRendering"/> for this conversion. When
    /// true the Skia backend renders glyphs with greyscale AA, integer x positions, and no font
    /// hinting — producing pixel-identical output across machines at the cost of slightly softer
    /// text. When null the static setting is used.
    /// </summary>
    public bool? DeterministicRendering { get; init; }

    /// <summary>
    /// When set, only pages within this 1-based inclusive range are emitted. Null (default)
    /// renders every page.
    /// </summary>
    public PageRange? Pages { get; init; }
}
