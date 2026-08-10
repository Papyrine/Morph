using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Where a sheet's merged regions fall, indexed for the per-cell questions the grid builder asks.
///
/// A worksheet declares merges once, as a list of ranges, but the dense grid needs to know about
/// them cell by cell: the top-left anchor carries the span, cells to its right must be dropped
/// entirely (their width is already accounted for by the anchor's <c>GridSpan</c>), and cells below
/// it become vertical-merge continuations that still occupy a position.
/// </summary>
sealed class MergeMap
{
    readonly Dictionary<(int Row, int Column), MergeInfo> anchors = [];
    readonly HashSet<(int Row, int Column)> coveredHorizontally = [];

    public static MergeMap For(S.Worksheet worksheet, SheetRange range)
    {
        var map = new MergeMap();
        var merges = worksheet.GetFirstChild<S.MergeCells>();
        if (merges == null)
        {
            return map;
        }

        foreach (var merge in merges.Elements<S.MergeCell>())
        {
            if (CellReference.ParseRange(merge.Reference?.Value) is not { } region)
            {
                continue;
            }

            var clipped = region.Intersect(range);
            if (clipped.IsEmpty)
            {
                continue;
            }

            for (var row = clipped.FirstRow; row <= clipped.LastRow; row++)
            {
                var isAnchorRow = row == clipped.FirstRow;
                map.anchors[(row, clipped.FirstColumn)] = new(
                    clipped.ColumnCount,
                    clipped.RowCount > 1
                        ? isAnchorRow ? VerticalMergeType.Restart : VerticalMergeType.Continue
                        : VerticalMergeType.None);

                for (var column = clipped.FirstColumn + 1; column <= clipped.LastColumn; column++)
                {
                    map.coveredHorizontally.Add((row, column));
                }
            }
        }

        return map;
    }

    /// <summary>The merge state of the cell at a position; a plain 1x1 when it is not merged.</summary>
    public MergeInfo At(int row, int column) =>
        anchors.TryGetValue((row, column), out var info) ? info : MergeInfo.None;

    /// <summary>
    /// Whether a position is swallowed by a merge starting to its left, and so must not be emitted
    /// at all.
    /// </summary>
    public bool IsCoveredHorizontally(int row, int column) => coveredHorizontally.Contains((row, column));
}

/// <summary>How one grid position participates in a merge.</summary>
readonly record struct MergeInfo(int ColumnSpan, VerticalMergeType VerticalMerge)
{
    public static MergeInfo None => new(1, VerticalMergeType.None);
}
