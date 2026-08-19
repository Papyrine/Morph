/// <summary>
/// Decorative borders drawn around each page (from w:pgBorders).
/// </summary>
sealed record PageBorders
{
    public BorderEdge Top { get; init; } = BorderEdge.None;
    public BorderEdge Right { get; init; } = BorderEdge.None;
    public BorderEdge Bottom { get; init; } = BorderEdge.None;
    public BorderEdge Left { get; init; } = BorderEdge.None;

    /// <summary>Inset from the page edge for the top border, in points.</summary>
    public double TopSpacePoints { get; init; } = 24;

    /// <summary>Inset from the page edge for the right border, in points.</summary>
    public double RightSpacePoints { get; init; } = 24;

    /// <summary>Inset from the page edge for the bottom border, in points.</summary>
    public double BottomSpacePoints { get; init; } = 24;

    /// <summary>Inset from the page edge for the left border, in points.</summary>
    public double LeftSpacePoints { get; init; } = 24;

    /// <summary>True when at least one edge is rendered.</summary>
    public bool HasAnyBorder => Top.IsVisible || Right.IsVisible || Bottom.IsVisible || Left.IsVisible;

    /// <summary>
    /// Whether the spaces measure from the text boundary (<c>w:pgBorders/@w:offsetFrom="text"</c>,
    /// the OOXML default) rather than from the page edge.
    /// </summary>
    public bool MeasureFromText { get; init; }

    /// <summary>
    /// The rectangle whose edges the painters stroke, in points — each edge line's CENTRE. In
    /// page mode the border's OUTER edge sits exactly the declared space from the page edge
    /// (Word-measured on page_borders/01: space=24pt, sz=3pt draws rows 50–55 at 150 DPI), so the
    /// line centre is the space plus half the stroke, growing inward. In text mode the space is
    /// the distance from the text boundary to the border's inner edge, growing outward.
    /// </summary>
    public (double X, double Y, double Width, double Height) EdgeRect(PageSettings settings)
    {
        double left, top, right, bottom;
        if (MeasureFromText)
        {
            left = settings.MarginLeft - LeftSpacePoints - Left.WidthPoints / 2;
            top = settings.MarginTop - TopSpacePoints - Top.WidthPoints / 2;
            right = settings.WidthPoints - settings.MarginRight + RightSpacePoints + Right.WidthPoints / 2;
            bottom = settings.HeightPoints - settings.MarginBottom + BottomSpacePoints + Bottom.WidthPoints / 2;
        }
        else
        {
            left = LeftSpacePoints + Left.WidthPoints / 2;
            top = TopSpacePoints + Top.WidthPoints / 2;
            right = settings.WidthPoints - RightSpacePoints - Right.WidthPoints / 2;
            bottom = settings.HeightPoints - BottomSpacePoints - Bottom.WidthPoints / 2;
        }

        return (left, top, right - left, bottom - top);
    }

    /// <summary>The four edges as the shared border-painting shape.</summary>
    public CellBorders Edges => new()
    {
        Top = Top,
        Right = Right,
        Bottom = Bottom,
        Left = Left
    };
}
