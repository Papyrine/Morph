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

    /// <summary>
    /// True when <c>w:pgMar/@w:top</c> was NEGATIVE. Per ECMA-376 §17.6.11 that means
    /// <see cref="MarginTop"/> is the absolute distance from the top of the page to the top of the
    /// body, so a header taller than the margin overlaps the body instead of pushing it down (see
    /// <c>RenderContextBase.SetPageHeaderBottom</c>). A positive margin is a MINIMUM and the body
    /// yields to the header.
    /// </summary>
    public bool TopMarginIsAbsolute { get; init; }

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

    /// <summary>
    /// Extra binding-edge margin in points (w:pgMar/@w:gutter). Applied to the left margin by default,
    /// or to the top margin when <see cref="GutterAtTop"/> is true.
    /// </summary>
    public double GutterPoints { get; init; }

    /// <summary>Restart value for this section's page numbering (<c>w:pgNumType/@w:start</c>).
    /// Null = continue from the previous section. Word's cover-page pattern is start=0: the
    /// cover displays 0 (usually unnumbered) and the next page displays 1.</summary>
    public int? PageNumberStart { get; init; }

    /// <summary>This section's page-number display format (<c>w:pgNumType/@w:fmt</c>), stored in
    /// the PAGE-field switch vocabulary ("roman"/"Roman"/"alphabetic"/"Alphabetic") so the field
    /// formatter consumes it directly. Null = decimal.</summary>
    public string? PageNumberFormat { get; init; }

    /// <summary>
    /// Whether the gutter is added to the top margin instead of the left margin
    /// (w:settings/w:gutterAtTop).
    /// </summary>
    public bool GutterAtTop { get; init; }

    public double ContentWidth => WidthPoints - MarginLeft - MarginRight;

    /// <summary>Width of a single column in points.</summary>
    public double ColumnWidth
    {
        get
        {
            if (ColumnCount > 1)
            {
                return (ContentWidth - ColumnSpacing * (ColumnCount - 1)) / ColumnCount;
            }

            return ContentWidth;
        }
    }
}
