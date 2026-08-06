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
/// with the production renderers (step 8.5 of <c>docs/layout-engine-proposal.md</c>); the engine holds
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

    public float PointsToPixels(float points) => points * Scale;
}
