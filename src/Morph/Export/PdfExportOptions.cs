namespace Morph;

/// <summary>
/// Options for the vector-text PDF exporter.
/// </summary>
public sealed record PdfExportOptions : ExportOptions
{
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
    /// When set, only pages within this 1-based inclusive range are rendered. Null (default)
    /// renders every page.
    /// </summary>
    public PageRange? Pages { get; init; }
}
