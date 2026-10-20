/// <summary>
/// Represents a row in a table.
/// </summary>
sealed class TableRow
{
    public required IReadOnlyList<TableCell> Cells { get; init; }

    /// <summary>
    /// Grid columns the row leaves empty before its first cell (<c>w:trPr/w:gridBefore</c>). Word-probed
    /// (<c>_probe_grid_before_after</c>, 2026-10-21): the row's first cell starts at this grid column, the
    /// skipped columns keep their grid widths, and nothing — no border, no shading — is drawn in them.
    /// Every walk of a row's cells starts its grid cursor here.
    /// </summary>
    public int GridBefore { get; init; }

    /// <summary>
    /// Grid columns the row leaves empty after its last cell (<c>w:trPr/w:gridAfter</c>). Drawn as
    /// nothing, like <see cref="GridBefore"/>; it counts only toward how many grid columns the row spans.
    /// </summary>
    public int GridAfter { get; init; }

    /// <summary>
    /// Explicit row height in points, if specified in the document.
    /// Null means the height should be calculated from content.
    /// </summary>
    public double? HeightPoints { get; init; }

    /// <summary>
    /// Whether the row height is exact (true) or minimum (false).
    /// When exact, the row will be exactly HeightPoints tall.
    /// When minimum, the row will be at least HeightPoints tall.
    /// </summary>
    public bool IsExactHeight { get; init; }

    /// <summary>
    /// Whether this row is marked as a header row (w:trPr/w:tblHeader).
    /// Header rows are intended to repeat at the top of each page when a table spans pages.
    /// Captured in the model; the renderer does not yet repeat them.
    /// </summary>
    public bool IsHeader { get; init; }

    /// <summary>
    /// Whether the row forbids being split across a page (<c>w:trPr/w:cantSplit</c>). Word-probed
    /// (<c>_probe_cantsplit_*</c>, 2026-08-06): without it a row that does not fit the space left splits
    /// there, even when it would fit a fresh page; with it the row moves whole instead — and a row taller
    /// than any page is NOT split either, it overflows the content area and clips at the paper edge.
    /// </summary>
    public bool CannotSplit { get; init; }

    /// <summary>
    /// Per-row outer-border override from <c>w:tblPrEx/w:tblBorders</c>.
    /// When set, replaces <see cref="TableProperties.DefaultBorders"/> when computing
    /// effective borders for this row's cells.
    /// </summary>
    public CellBorders? OverrideBorders { get; init; }

    /// <summary>
    /// Per-row inside-horizontal border override from <c>w:tblPrEx/w:tblBorders/w:insideH</c>.
    /// </summary>
    public BorderEdge? OverrideInsideHBorder { get; init; }

    /// <summary>
    /// Per-row inside-vertical border override from <c>w:tblPrEx/w:tblBorders/w:insideV</c>.
    /// </summary>
    public BorderEdge? OverrideInsideVBorder { get; init; }

    /// <summary>
    /// Per-row default cell padding override from <c>w:tblPrEx/w:tblCellMar</c>.
    /// </summary>
    public CellSpacing? OverrideCellPadding { get; init; }
}