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
    /// The rectangle whose edges the painters stroke, in points — each edge's INNER face, from
    /// which its stack grows outward (<c>BorderStroke.Scope.Page</c>). XPS-read on <c>_probe_pgbdr</c>
    /// (2026-09-05): in text mode the inner face sits exactly the declared space outside the text
    /// boundary (space 24pt on a 72pt margin puts it at 48.0 for 0.75, 3 and 6pt singles alike, the
    /// stack growing outward from there); in page mode the OUTER face sits the space from the page
    /// edge (24.0 for a 3pt single), so the inner face is the space plus the drawn stack.
    /// </summary>
    public (double X, double Y, double Width, double Height) EdgeRect(PageSettings settings)
    {
        double left, top, right, bottom;
        if (MeasureFromText)
        {
            left = settings.MarginLeft - LeftSpacePoints;
            top = settings.MarginTop - TopSpacePoints;
            right = settings.WidthPoints - settings.MarginRight + RightSpacePoints;
            bottom = settings.HeightPoints - settings.MarginBottom + BottomSpacePoints;
        }
        else
        {
            left = LeftSpacePoints + BorderStroke.DrawnStack(Left);
            top = TopSpacePoints + BorderStroke.DrawnStack(Top);
            right = settings.WidthPoints - RightSpacePoints - BorderStroke.DrawnStack(Right);
            bottom = settings.HeightPoints - BottomSpacePoints - BorderStroke.DrawnStack(Bottom);
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
