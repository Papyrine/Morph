/// <summary>
/// Page settings extracted from the document.
/// </summary>
sealed record PageSettings
{
    /// <summary>Page width in points (1/72 inch). Defaults to A4; use DefaultPageSize for region-based defaults.</summary>
    public double WidthPoints { get; init; } = 595.28;

    /// <summary>Page height in points (1/72 inch). Defaults to A4; use DefaultPageSize for region-based defaults.</summary>
    public double HeightPoints { get; init; } = 841.89;

    /// <summary>Top margin in points.</summary>
    // 1 inch
    public double MarginTop { get; init; } = 72;

    /// <summary>Bottom margin in points.</summary>
    public double MarginBottom { get; init; } = 72;

    /// <summary>Left margin in points.</summary>
    public double MarginLeft { get; init; } = 72;

    /// <summary>Right margin in points.</summary>
    public double MarginRight { get; init; } = 72;

    /// <summary>Distance from top edge to header in points.</summary>
    // 0.5 inch
    public double HeaderDistance { get; init; } = 36;

    /// <summary>Distance from bottom edge to footer in points.</summary>
    // 0.5 inch
    public double FooterDistance { get; init; } = 36;

    /// <summary>Number of columns (1 = single column layout).</summary>
    public int ColumnCount { get; init; } = 1;

    /// <summary>Space between columns in points.</summary>
    // 0.5 inch default
    public double ColumnSpacing { get; init; } = 36;

    /// <summary>Line numbering settings for this section. Null if line numbers are disabled.</summary>
    public LineNumberSettings? LineNumbers { get; init; }

    /// <summary>
    /// Document grid line pitch in points (from w:docGrid/@w:linePitch).
    /// </summary>
    public double DocumentGridLinePitchPoints { get; init; }

    /// <summary>
    /// Count of w:lastRenderedPageBreak markers in the source document.
    /// </summary>
    public int LastRenderedPageBreakCount { get; init; }

    /// <summary>
    /// Page background color (hex). Null for white/transparent.
    /// </summary>
    public string? BackgroundColorHex { get; init; }

    /// <summary>
    /// Whether the first page has different header/footer (w:titlePg).
    /// When true, the default header/footer should not appear on page 1.
    /// </summary>
    public bool DifferentFirstPage { get; init; }

    /// <summary>
    /// Decorative borders drawn around each page (from w:pgBorders). Null when no page borders are defined.
    /// </summary>
    public PageBorders? PageBorders { get; init; }

    public double ContentWidth => WidthPoints - MarginLeft - MarginRight;

    /// <summary>Width of a single column in points.</summary>
    public double ColumnWidth => ColumnCount > 1
        ? (ContentWidth - ColumnSpacing * (ColumnCount - 1)) / ColumnCount
        : ContentWidth;
}