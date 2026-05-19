/// <summary>
/// Cell-level properties.
/// </summary>
sealed record TableCellProperties
{
    public double? WidthPoints { get; init; }
    public string? BackgroundColorHex { get; init; }

    /// <summary>Cell padding (inset from border to content). Null means use table default.</summary>
    public CellSpacing? Padding { get; init; }

    /// <summary>Cell margin (space outside the border). Null means use table default.</summary>
    public CellSpacing? Margin { get; init; }

    /// <summary>Per-edge border specifications. Null means use table default borders.</summary>
    public CellBorders? Borders { get; init; }

    /// <summary>
    /// Cell-level diagonal borders (<c>w:tl2br</c> / <c>w:tr2bl</c>). Diagonals don't
    /// participate in the 4-side cell→table cascade — they're applied additively on top
    /// of whatever borders the cell ends up with.
    /// </summary>
    public CellDiagonals? Diagonals { get; init; }

    /// <summary>Number of grid columns this cell spans. Default is 1.</summary>
    public int GridSpan { get; init; } = 1;

    /// <summary>Vertical alignment of content within the cell. Default is Top.</summary>
    public CellVerticalAlignment VerticalAlignment { get; init; } = CellVerticalAlignment.Top;

    /// <summary>Vertical merge state for this cell. Default is None.</summary>
    public VerticalMergeType VerticalMerge { get; init; } = VerticalMergeType.None;

    /// <summary>
    /// Text flow direction within the cell (w:textDirection). Default is left-to-right.
    /// </summary>
    public CellTextDirection TextDirection { get; init; } = CellTextDirection.LeftToRight;

    /// <summary>
    /// <c>w:hideMark</c> — when true, the end-of-cell paragraph mark is suppressed for
    /// height measurement so the cell can collapse below one line of text. Set per cell;
    /// only affects cells whose sole content is an empty paragraph.
    /// </summary>
    public bool HideMark { get; init; }

    /// <summary>
    /// <c>w:noWrap</c> — when true, cell content shall not wrap to a new line. In
    /// auto-fit tables, the column should expand to fit the longest run; in fixed-layout
    /// tables, content can overflow. Morph parses the flag but doesn't currently grow
    /// columns based on it (cells with explicit <c>w:tcW</c> use that width verbatim).
    /// </summary>
    public bool NoWrap { get; init; }
}