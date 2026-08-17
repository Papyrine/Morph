/// <summary>
/// Backend-independent drawing context: the font-resolution settings every backend's renderer needs,
/// the page geometry in device pixels, and points-to-pixels conversion. Concrete subclasses
/// (<c>SkiaRenderContext</c>, <c>ImageSharpRenderContext</c>, <c>PdfRenderContext</c>) add the font,
/// brush and image caches over their own drawing library.
///
/// <para>This used to carry the pagination state as well — the flow cursor, page and column advance,
/// header/footer reservation, float-wrap exclusions, contextual-spacing carry, page numbering and line
/// numbering — because the production page renderers paginated as they drew. The layout engine
/// separates the two: the <c>Fragmenter</c> decides every position up front and each painter draws the
/// resulting <c>LaidOutDocument</c> without measuring or advancing anything. All of that state went
/// with the production renderers (step 8.5 of <c>docs/layout-engine.md</c>); the engine holds
/// the equivalents as its own locals, where they cannot leak between pages.</para>
/// </summary>
abstract class RenderContextBase
{
    public PageSettings PageSettings { get; }
    public CompatibilitySettings Compatibility { get; }
    public int Dpi { get; }
    public float Scale { get; }

    /// <summary>
    /// Scale factor for font width measurements. Values > 1.0 make text wider (earlier line wrapping).
    /// </summary>
    public float FontWidthScale { get; }
    public Func<string, string?>? FontFallback { get; }

    /// <summary>
    /// When non-null, font resolution uses only files from this directory (recursive)
    /// and all system/user/Office/cloud caches and OS-level fallbacks are skipped.
    /// Missing fonts throw.
    /// </summary>
    public string? FontDirectory { get; }

    /// <summary>
    /// When <c>true</c>, Skia renders glyphs with greyscale AA, integer x positions
    /// and no hinting for pixel-stable output across machines. Sourced from
    /// <see cref="ImageExportOptions.DeterministicRendering"/> or the
    /// <see cref="DefaultFontSettings.DeterministicRendering"/> static fallback.
    /// </summary>
    public bool DeterministicRendering { get; }

    /// <summary>
    /// When <c>true</c> the backend clears each new page to full transparency instead of the
    /// page background colour (or white). Used by the standalone WordArt rasterizers so the
    /// produced PNG composites cleanly when embedded elsewhere (e.g. into a PDF).
    /// </summary>
    public bool TransparentBackground { get; init; }

    // Page dimensions in pixels
    public int PageWidthPixels { get; }
    public int PageHeightPixels { get; }

    // Content area in points. With pagination gone these are plain projections of the page settings —
    // the standalone WordArt rasterizers place their single figure against them.
    public float ContentLeft => (float) PageSettings.MarginLeft;
    public float ContentTop => (float) PageSettings.MarginTop;
    public float ContentWidth => (float) PageSettings.ColumnWidth;

    protected RenderContextBase(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility, double fontWidthScale, Func<string, string?>? fontFallback = null, string? fontDirectory = null, bool? deterministicRendering = null)
    {
        PageSettings = pageSettings;
        Compatibility = compatibility ?? new CompatibilitySettings();
        Dpi = dpi;
        Scale = dpi / 72f;
        FontWidthScale = (float) fontWidthScale;
        FontFallback = fontFallback;
        FontDirectory = fontDirectory;
        DeterministicRendering = deterministicRendering ?? DefaultFontSettings.DeterministicRendering;

        PageWidthPixels = ToPagePixels(pageSettings.WidthPoints, dpi);
        PageHeightPixels = ToPagePixels(pageSettings.HeightPoints, dpi);
    }

    // Must be computed in double precision rather than via the float Scale. For common page sizes
    // the exact pixel count is a whole number (US Letter at 150 DPI is 612x792pt -> 1275x1650px),
    // and float Scale is a hair below the true dpi/72 ratio, which drags the product just under the
    // boundary (1274.99995) so the truncation loses a whole pixel off each axis.
    static int ToPagePixels(double points, int dpi) => (int) (points * dpi / 72.0);

    // Pixel dimensions for a specific page's geometry. The layout engine records the settings each page
    // was laid at (a section break can switch page size mid-document), so a painter sizes each page from
    // its own settings rather than the context's initial page size.
    public (int Width, int Height) PagePixels(PageSettings settings) =>
        (ToPagePixels(settings.WidthPoints, Dpi), ToPagePixels(settings.HeightPoints, Dpi));

    /// <summary>
    /// The rectangle of a page a <paramref name="crop"/> emits, in device pixels. Same per-page
    /// settings rule as <see cref="PagePixels"/>: a section break can change the margins as well as
    /// the paper, so the rectangle is resolved per page rather than once per document.
    /// </summary>
    public (int X, int Y, int Width, int Height) PageRect(PageSettings settings, PageCrop crop)
    {
        var (width, height) = PagePixels(settings);
        if (crop == PageCrop.FullPage)
        {
            return (0, 0, width, height);
        }

        // The gutter is folded into MarginLeft (or MarginTop under w:gutterAtTop) at parse time by
        // DocumentParser.ExtractPageSettings, so adding it here would charge for it twice.
        var left = settings.MarginLeft;
        var right = settings.MarginRight;
        var top = settings.MarginTop;
        var bottom = settings.MarginBottom;

        if (crop == PageCrop.ContentBoxWithHeaderFooter)
        {
            // The bands sit at HeaderDistance from the top edge and FooterDistance from the bottom
            // (Fragmenter.HeaderBand/FooterBand), normally inside the margin. Min rather than the
            // distance outright, so the unusual document whose header sits below its top margin
            // does not have the crop pushed back out past it.
            top = Math.Min(top, settings.HeaderDistance);
            bottom = Math.Min(bottom, settings.FooterDistance);
        }

        // Both edges go through ToPagePixels rather than scaling a width, so the crop lands on the
        // same integer grid as the full page and a FullPage rect is exactly PagePixels. An integer
        // origin is also what keeps the result a pure crop: a fractional one would have to resample.
        var x = Clamp(ToPagePixels(left, Dpi), 0, width - 1);
        var y = Clamp(ToPagePixels(top, Dpi), 0, height - 1);
        return (
            x,
            y,
            Clamp(ToPagePixels(settings.WidthPoints - right, Dpi) - x, 1, width - x),
            Clamp(ToPagePixels(settings.HeightPoints - bottom, Dpi) - y, 1, height - y));
    }

    // Margins are not validated against the paper anywhere upstream, so a document declaring more
    // margin than it has page must still produce a drawable rectangle rather than a negative one.
    static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), Math.Max(min, max));

    public float PointsToPixels(float points) => points * Scale;
}
