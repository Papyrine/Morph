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

    /// <summary>
    /// How much of the paper page each image covers. The default emits the whole sheet; the other
    /// values crop away the margins, which is what a thumbnail or an embedded preview usually
    /// wants and otherwise obliges the caller to re-derive the document's margins for itself.
    ///
    /// <para>This is a crop, not a re-layout: the page is painted exactly as it would have been
    /// and a rectangle of it is emitted, so pagination and line breaking never move and
    /// <see cref="Dpi"/> still governs the scale. What lands outside the rectangle is lost —
    /// headers and footers under <see cref="PageCrop.ContentBox"/>, and under either cropping
    /// value any page-anchored art that bleeds past the content box.</para>
    /// </summary>
    public PageCrop Crop { get; init; } = PageCrop.FullPage;
}
