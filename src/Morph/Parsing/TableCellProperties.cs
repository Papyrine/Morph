/// <summary>
/// Cell-level properties.
/// </summary>
sealed record TableCellProperties
{
    public double? WidthPoints { get; init; }

    /// <summary>Preferred width as a fraction of the table width (<c>w:tcW w:type="pct"</c>,
    /// stored in fiftieths of a percent in OOXML — 2500 means half). Resolved against the
    /// table's available width at layout time.</summary>
    public double? WidthFraction { get; init; }

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

    /// <summary>
    /// The cell's text is laid out on ONE line however narrow the cell is — the line runs past
    /// the cell's edge rather than breaking.
    ///
    /// Distinct from <see cref="NoWrap"/>, which is Word's <c>w:noWrap</c>: that one is a hint to
    /// an AUTO-FIT table's column sizing (grow the column to the longest run) and lets the text
    /// wrap anyway once the column is fixed. This one is unconditional, and exists for the
    /// spreadsheet path, where wrapping is opt-in per cell (<c>alignment/@wrapText</c>) rather
    /// than the default: a spreadsheet cell without it overflows or is clipped, never wraps.
    /// </summary>
    public bool SingleLine { get; init; }

    /// <summary>
    /// The cell's CONTENT is clipped to the cell box — shading and borders still draw in full.
    /// Content that runs past the box (a <see cref="SingleLine"/> line too wide for its column, or
    /// wrapped text too tall for a pinned row) disappears at the edge instead of painting over the
    /// neighbour.
    ///
    /// This is Excel's law and the spreadsheet path sets it on every cell. Excel's overflow is
    /// permitted only across EMPTY neighbours — <see cref="SheetGridBuilder.OverflowSpan"/> has
    /// already widened the cell over those, so the cell box is exactly the area Excel draws in — and
    /// stops dead at the first occupied one. Word has no equivalent rule for a normal row, so DOCX
    /// and HTML leave it false and keep drawing their overflow.
    /// </summary>
    public bool ClipOverflow { get; init; }

    /// <summary>
    /// How far the <see cref="ClipOverflow"/> clip reaches OUTSIDE the cell box, in points, on the
    /// left and right. Both are zero for a cell whose ink stops at its own edges.
    ///
    /// Excel permits overflow across empty neighbours in the direction the alignment implies, and
    /// only one of those directions can be expressed by widening the cell: rightwards, which
    /// <see cref="SheetGridBuilder.OverflowSpan"/> already does for LEFT-aligned text (so these stay
    /// zero for it). Right-aligned text spills left and centred text spills both ways, neither of
    /// which a dense positional grid can express as a box — a cell cannot begin left of its own
    /// column — so the clip carries the reach instead.
    /// </summary>
    public double ClipSpillLeftPoints { get; init; }

    /// <inheritdoc cref="ClipSpillLeftPoints"/>
    public double ClipSpillRightPoints { get; init; }
}