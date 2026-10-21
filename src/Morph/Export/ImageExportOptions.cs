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
    /// How an Excel workbook is divided into images: as it prints, which is the default, or one
    /// image for each sheet. Only the Excel converters read it. Under
    /// <see cref="SheetPagination.OnePagePerSheet"/> a page is a sheet, so <see cref="Pages"/>
    /// chooses among the visible sheets.
    /// </summary>
    public SheetPagination SheetPagination { get; init; } = SheetPagination.PrintLayout;

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

    /// <summary>
    /// How many pages are painted at once. The default, 1, paints them one after another and hands
    /// each to the caller as it is finished. Above 1, pages are painted on that many threads — each
    /// with its own render context, so the output is identical — and handed over in page order once
    /// all are painted. Layout is unaffected; only the painting and PNG encoding are spread. Worth
    /// raising for a document of many pages when nothing else is competing for the cores.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>
    /// The most pixels one page may take. A document whose largest page would exceed it at
    /// <see cref="Dpi"/> is painted at the highest resolution that fits instead, so a crafted page size
    /// cannot ask for a raster of gigabytes. The default, 50 million, is an A4 page at 600 DPI with room
    /// to spare; 0 lifts the limit.
    /// </summary>
    public long MaxPagePixels { get; init; } = 50_000_000;

    // The conversion's cancellation, set by the converters from their own cancel parameter so it rides
    // to the render loop with the options it already reaches.
    internal Cancel Cancellation { get; init; }

    /// <summary>
    /// The resolution pages are painted at: <see cref="Dpi"/>, or lower when the largest page would
    /// exceed <see cref="MaxPagePixels"/> at it.
    /// </summary>
    internal int EffectiveDpi(IEnumerable<PageSettings> pages)
    {
        if (MaxPagePixels <= 0)
        {
            return Dpi;
        }

        var largest = pages.Select(_ => _.WidthPoints * _.HeightPoints).DefaultIfEmpty(0).Max();
        if (largest <= 0)
        {
            return Dpi;
        }

        var fitting = (int) Math.Floor(72 * Math.Sqrt(MaxPagePixels / largest));
        return Math.Clamp(fitting, 1, Dpi);
    }
}
