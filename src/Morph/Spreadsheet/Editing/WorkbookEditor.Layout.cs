using S = DocumentFormat.OpenXml.Spreadsheet;

static partial class WorkbookEditor
{
    /// <summary>
    /// Sets columns' width, in characters of the body font's digits as the file counts them (the
    /// <c>width</c> of <c>col</c>, its padding included), as dragging a column's edge in Excel does.
    /// </summary>
    public static byte[] SetColumnWidth(byte[] xlsx, int sheet, int firstColumn, int lastColumn, double width) =>
        Edit(xlsx, null, _ =>
        {
            var worksheet = Worksheet(_, sheet);
            var columns = worksheet.GetFirstChild<S.Columns>();
            if (columns == null)
            {
                columns = new();
                worksheet.InsertBefore(columns, worksheet.GetFirstChild<S.SheetData>());
            }

            // The spans that overlap are cut around the columns set, keeping what they say of the rest.
            var spans = columns.Elements<S.Column>().ToList();
            var kept = new List<S.Column>();
            var covered = new HashSet<int>();
            foreach (var span in spans)
            {
                var min = (int) (span.Min?.Value ?? 0);
                var max = (int) (span.Max?.Value ?? 0);
                span.Remove();
                if (max < firstColumn ||
                    min > lastColumn)
                {
                    kept.Add(span);
                    continue;
                }

                if (min < firstColumn)
                {
                    kept.Add(Span(span, min, firstColumn - 1));
                }

                if (max > lastColumn)
                {
                    kept.Add(Span(span, lastColumn + 1, max));
                }

                var inside = Span(span, Math.Max(min, firstColumn), Math.Min(max, lastColumn));
                inside.Width = width;
                inside.CustomWidth = true;
                inside.Hidden = null;
                kept.Add(inside);
                for (var column = Math.Max(min, firstColumn); column <= Math.Min(max, lastColumn); column++)
                {
                    covered.Add(column);
                }
            }

            // Columns no span named yet.
            var start = -1;
            for (var column = firstColumn; column <= lastColumn + 1; column++)
            {
                var open = column <= lastColumn && !covered.Contains(column);
                if (open && start < 0)
                {
                    start = column;
                }
                else if (!open && start >= 0)
                {
                    kept.Add(new()
                    {
                        Min = (uint) start,
                        Max = (uint) (column - 1),
                        Width = width,
                        CustomWidth = true
                    });
                    start = -1;
                }
            }

            foreach (var span in kept.OrderBy(_ => _.Min?.Value ?? 0))
            {
                columns.AppendChild(span);
            }
        });

    static S.Column Span(S.Column source, int min, int max)
    {
        var copy = (S.Column) source.CloneNode(true);
        copy.Min = (uint) min;
        copy.Max = (uint) max;
        return copy;
    }

    /// <summary>Sets rows' height in points, as dragging a row's edge in Excel does.</summary>
    public static byte[] SetRowHeight(byte[] xlsx, int sheet, int firstRow, int lastRow, double points) =>
        Edit(xlsx, null, _ =>
        {
            var worksheet = Worksheet(_, sheet);
            var data = worksheet.GetFirstChild<S.SheetData>()!;
            for (var row = firstRow; row <= lastRow; row++)
            {
                var line = RowAt(data, row);
                line.Height = points;
                line.CustomHeight = true;
                line.Hidden = null;
            }
        });
}
