/// <summary>
/// Base class for rendering context state shared across backends.
/// Manages pagination, column layout, line numbering, and coordinate conversion.
/// </summary>
abstract class RenderContextBase
{
    public PageSettings PageSettings { get; private set; }
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

    // Header/footer space adjustments
    float headerSpace;
    float footerSpace;

    // Current position on the page (in points)
    public float CurrentY { get; set; }
    public int CurrentPageNumber { get; private set; } = 1;
    public int CurrentColumn { get; private set; }

    /// <summary>
    /// Total number of pages the document produces, or 0 when not yet known. Set from a counting
    /// pass before the real render when the document contains a NUMPAGES/SECTIONPAGES field
    /// (<see cref="ParsedDocument.RequiresTotalPageCount"/>); consumed when resolving those fields.
    /// </summary>
    public int TotalPageCount { get; set; }

    // Line numbering state
    int currentLineNumber = 1;

    // Contextual spacing state
    public bool LastParagraphHadContextualSpacing { get; set; }
    public float LastParagraphSpacingAfterPoints { get; set; }
    public string? LastParagraphStyleId { get; set; }

    /// <summary>
    /// When true, the next paragraph's top border should be suppressed because
    /// the previous paragraph collapsed their shared w:between border.
    /// </summary>
    public bool SuppressNextParagraphTopBorder { get; set; }

    /// <summary>
    /// One-shot: the next flow paragraph starts at the top of a page produced by an automatic
    /// break, where Word does not apply the paragraph's spacing-before. Set by the page
    /// renderers immediately before rendering a body paragraph; consumed and cleared by the
    /// text engines' spacing-before logic.
    /// </summary>
    public bool SuppressPageTopSpacingBefore { get; set; }

    // Page dimensions in pixels
    public int PageWidthPixels { get; private set; }
    public int PageHeightPixels { get; private set; }

    // Full content area bounds (before column division)
    float FullContentLeft => (float) PageSettings.MarginLeft;
    float FullContentTop => (float) PageSettings.MarginTop + headerSpace;
    float FullContentBottom => (float) (PageSettings.HeightPoints - PageSettings.MarginBottom) - footerSpace;

    // Current column content area bounds in points
    public float ContentLeft => containerLeftOverride ?? FullContentLeft + CurrentColumn * ((float) PageSettings.ColumnWidth + (float) PageSettings.ColumnSpacing);
    public float ContentTop => FullContentTop;
    public float ContentBottom => FullContentBottom;
    public float ContentWidth => containerWidthOverride ?? (float) PageSettings.ColumnWidth;
    public float ContentHeight => FullContentBottom - FullContentTop;

    // Nested-content overrides: when a nested table or framed block needs the layout pipeline
    // to size to a sub-area of the page (e.g. a parent cell), callers can push the container
    // bounds via PushContentContainer / pop with the returned IDisposable.
    float? containerLeftOverride;
    float? containerWidthOverride;

    public IDisposable PushContentContainer(float left, float width)
    {
        var previousLeft = containerLeftOverride;
        var previousWidth = containerWidthOverride;
        containerLeftOverride = left;
        containerWidthOverride = width;
        return new ContainerScope(this, previousLeft, previousWidth);
    }

    sealed class ContainerScope(RenderContextBase context, float? previousLeft, float? previousWidth) : IDisposable
    {
        public void Dispose()
        {
            context.containerLeftOverride = previousLeft;
            context.containerWidthOverride = previousWidth;
        }
    }

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

        PageWidthPixels = (int) (pageSettings.WidthPoints * Scale);
        PageHeightPixels = (int) (pageSettings.HeightPoints * Scale);

        CurrentY = ContentTop;
    }

    public void SetHeaderFooterSpace(float headerHeight, float footerHeight)
    {
        var headerEnd = (float) PageSettings.HeaderDistance + headerHeight;
        if (headerHeight > 0 &&
            headerEnd > (float) PageSettings.MarginTop)
        {
            headerSpace = headerEnd - (float) PageSettings.MarginTop;
        }
        else
        {
            headerSpace = 0;
        }

        var footerEnd = (float) PageSettings.FooterDistance + footerHeight;
        if (footerHeight > 0 &&
            footerEnd > (float) PageSettings.MarginBottom)
        {
            footerSpace = footerEnd - (float) PageSettings.MarginBottom;
        }
        else
        {
            footerSpace = 0;
        }

        CurrentY = ContentTop;
    }

    public void StartNewPage()
    {
        CurrentPageNumber++;
        CurrentColumn = 0;
        CurrentY = ContentTop;
        floatExclusions.Clear();
    }

    // ---- float text-wrap exclusions ----

    // Rectangles (points, absolute page coordinates) that body-flow paragraphs must not
    // overlap, registered when a floating image with a wrapping mode (wp:wrapSquare /
    // wrapTight / wrapThrough / wrapTopAndBottom) renders. Page-scoped: cleared when a new
    // page starts. wrapNone / behind-text floats register nothing — overlap is their design.
    readonly List<FloatExclusion> floatExclusions = [];

    readonly record struct FloatExclusion(float Left, float Top, float Right, float Bottom, bool FullWidth, WrapTextSide Side);

    public void RegisterFloatExclusion(FloatingImageElement image, float leftPoints, float topPoints, float widthPoints, float heightPoints)
    {
        if (image.BehindText)
        {
            return;
        }

        // Tight/Through wrap along the image outline in Word; the rectangular extent is the
        // v1 approximation for both (same as Square).
        switch (image.WrapType)
        {
            case WrapType.Square or WrapType.Tight or WrapType.Through:
                floatExclusions.Add(new(
                    leftPoints - (float) image.WrapDistanceLeftPoints,
                    topPoints - (float) image.WrapDistanceTopPoints,
                    leftPoints + widthPoints + (float) image.WrapDistanceRightPoints,
                    topPoints + heightPoints + (float) image.WrapDistanceBottomPoints,
                    FullWidth: false,
                    image.WrapTextSide));
                break;
            case WrapType.TopAndBottom:
                floatExclusions.Add(new(
                    ContentLeft,
                    topPoints - (float) image.WrapDistanceTopPoints,
                    ContentLeft + ContentWidth,
                    topPoints + heightPoints + (float) image.WrapDistanceBottomPoints,
                    FullWidth: true,
                    WrapTextSide.BothSides));
                break;
        }
    }

    /// <summary>
    /// Resolves where a flow paragraph starting at <paramref name="y"/> can lay out, given the
    /// active float exclusions: the widest free horizontal segment of the content area beside
    /// the floats. When no usable segment exists at <paramref name="y"/> (a wrapTopAndBottom
    /// band, or floats covering the whole measure), Y advances below the blocking floats.
    /// Constrained is false when the paragraph gets the full content width.
    /// </summary>
    public (float X, float Width, float Y, bool Constrained) ResolveFlowBand(float y)
    {
        var contentLeft = ContentLeft;
        var contentRight = ContentLeft + ContentWidth;
        if (floatExclusions.Count == 0)
        {
            return (contentLeft, contentRight - contentLeft, y, false);
        }

        // The paragraph's exact first-line height isn't known yet; probe with a nominal line so
        // a paragraph starting just above a float still wraps around it.
        const float probeHeight = 12f;
        // Below this the band is unusable — skip below the floats instead (half an inch, about
        // where Word's own wrapping stops squeezing words in).
        const float minUsableWidth = 36f;

        var currentY = y;
        for (var guard = 0; guard < 8; guard++)
        {
            float? clearTo = null;
            var segments = new List<(float Start, float End)>
            {
                (contentLeft, contentRight)
            };
            foreach (var exclusion in floatExclusions)
            {
                if (currentY + probeHeight <= exclusion.Top || currentY >= exclusion.Bottom)
                {
                    continue;
                }

                clearTo = clearTo is { } clear ? Math.Max(clear, exclusion.Bottom) : exclusion.Bottom;
                var blockLeft = exclusion.FullWidth ? contentLeft : Math.Max(contentLeft, exclusion.Left);
                var blockRight = exclusion.FullWidth ? contentRight : Math.Min(contentRight, exclusion.Right);
                var remaining = new List<(float Start, float End)>();
                foreach (var (start, end) in segments)
                {
                    if (blockRight <= start || blockLeft >= end)
                    {
                        remaining.Add((start, end));
                        continue;
                    }

                    // An explicit @wrapText side restricts which side of THIS float text may
                    // use; BothSides/Largest leave both free segments available (the caller
                    // takes the widest — Word's "largest" — since a single band can't carry
                    // both sides at once).
                    if (blockLeft > start && exclusion.Side != WrapTextSide.Right)
                    {
                        remaining.Add((start, blockLeft));
                    }

                    if (blockRight < end && exclusion.Side != WrapTextSide.Left)
                    {
                        remaining.Add((blockRight, end));
                    }
                }

                segments = remaining;
            }

            if (clearTo == null)
            {
                return (contentLeft, contentRight - contentLeft, currentY, false);
            }

            var bestStart = 0f;
            var bestWidth = 0f;
            foreach (var (start, end) in segments)
            {
                if (end - start > bestWidth)
                {
                    bestStart = start;
                    bestWidth = end - start;
                }
            }

            if (bestWidth >= minUsableWidth)
            {
                return (bestStart, bestWidth, currentY, true);
            }

            currentY = clearTo.Value;
        }

        return (contentLeft, contentRight - contentLeft, currentY, false);
    }

    public bool MoveToNextColumn()
    {
        if (CurrentColumn < PageSettings.ColumnCount - 1)
        {
            CurrentColumn++;
            CurrentY = ContentTop;
            return true;
        }

        return false;
    }

    public void ResetColumn() =>
        CurrentColumn = 0;

    public void UpdatePageSettings(PageSettings newSettings)
    {
        PageSettings = newSettings;
        PageWidthPixels = (int) (newSettings.WidthPoints * Scale);
        PageHeightPixels = (int) (newSettings.HeightPoints * Scale);
    }

    public bool HasSpaceFor(float heightPoints)
    {
        var tolerance = ContentHeight * 0.02f;
        return CurrentY + heightPoints <= ContentBottom + tolerance;
    }

    public float PointsToPixels(float points) => points * Scale;

    public int GetNextLineNumber() =>
        currentLineNumber++;

    public void ResetLineNumbersForPage()
    {
        if (PageSettings.LineNumbers?.Restart == LineNumberRestart.NewPage)
        {
            currentLineNumber = PageSettings.LineNumbers.Start;
        }
    }

    public void ResetLineNumbersForSection()
    {
        if (PageSettings.LineNumbers?.Restart is LineNumberRestart.NewSection or LineNumberRestart.NewPage)
        {
            currentLineNumber = PageSettings.LineNumbers.Start;
        }
    }

    public void InitializeLineNumbers()
    {
        if (PageSettings.LineNumbers != null)
        {
            currentLineNumber = PageSettings.LineNumbers.Start;
        }
    }
}
