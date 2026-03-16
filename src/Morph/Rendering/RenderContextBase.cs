namespace WordRender.Rendering;

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

    // Header/footer space adjustments
    float headerSpace;
    float footerSpace;

    // Current position on the page (in points)
    public float CurrentY { get; set; }
    public int CurrentPageNumber { get; private set; } = 1;
    public int CurrentColumn { get; private set; }

    // Line numbering state
    int currentLineNumber = 1;

    // Contextual spacing state
    public bool LastParagraphHadContextualSpacing { get; set; }
    public float LastParagraphSpacingAfterPoints { get; set; }
    public string? LastParagraphStyleId { get; set; }

    // Page dimensions in pixels
    public int PageWidthPixels { get; private set; }
    public int PageHeightPixels { get; private set; }

    // Full content area bounds (before column division)
    float FullContentLeft => (float) PageSettings.MarginLeft;
    float FullContentTop => (float) PageSettings.MarginTop + headerSpace;
    float FullContentBottom => (float) (PageSettings.HeightPoints - PageSettings.MarginBottom) - footerSpace;

    // Current column content area bounds in points
    public float ContentLeft => FullContentLeft + CurrentColumn * ((float) PageSettings.ColumnWidth + (float) PageSettings.ColumnSpacing);
    public float ContentTop => FullContentTop;
    public float ContentBottom => FullContentBottom;
    public float ContentWidth => (float) PageSettings.ColumnWidth;
    public float ContentHeight => FullContentBottom - FullContentTop;

    protected RenderContextBase(PageSettings pageSettings, int dpi, CompatibilitySettings? compatibility, double fontWidthScale, Func<string, string?>? fontFallback = null)
    {
        PageSettings = pageSettings;
        Compatibility = compatibility ?? new CompatibilitySettings();
        Dpi = dpi;
        Scale = dpi / 72f;
        FontWidthScale = (float) fontWidthScale;
        FontFallback = fontFallback;

        PageWidthPixels = (int) (pageSettings.WidthPoints * Scale);
        PageHeightPixels = (int) (pageSettings.HeightPoints * Scale);

        CurrentY = ContentTop;
    }

    public void SetHeaderFooterSpace(float headerHeight, float footerHeight)
    {
        var headerEnd = (float) PageSettings.HeaderDistance + headerHeight;
        if (headerHeight > 0 && headerEnd > (float) PageSettings.MarginTop)
        {
            headerSpace = headerEnd - (float) PageSettings.MarginTop;
        }
        else
        {
            headerSpace = 0;
        }

        var footerEnd = (float) PageSettings.FooterDistance + footerHeight;
        if (footerHeight > 0 && footerEnd > (float) PageSettings.MarginBottom)
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
