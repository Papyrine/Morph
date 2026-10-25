// Where a mouse step lands, in physical pixels: a glyph's rectangle from Window.GetPoint for text
// positions, and the geometry around a table or the page for the hot spots (selection bar, cell edge,
// column top, row bar, move handle). The hot-spot offsets are calibrated by hovering and reading the
// cursor Word shows; the smoke test stores them in Results/environment.json.
sealed class HotspotCalibration
{
    public Dictionary<string, int> Offsets { get; } = new(StringComparer.Ordinal)
    {
        ["selectionBarInset"] = 0,
        ["cellLeftEdgeInset"] = 4,
        ["columnTopAbove"] = 3,
        ["rowBarLeft"] = 30,
        ["tableHandleOffset"] = 14
    };

    public static HotspotCalibration Load()
    {
        var calibration = new HotspotCalibration();
        var environment = ResultStore.LoadEnvironment();
        if (environment != null)
        {
            foreach (var (name, value) in environment.HotspotOffsets)
            {
                calibration.Offsets[name] = value;
            }
        }

        return calibration;
    }
}

sealed class Hotspots
{
    readonly ProbeDocument probe;
    readonly HotspotCalibration calibration;

    public Hotspots(ProbeDocument probe, HotspotCalibration calibration)
    {
        this.probe = probe;
        this.calibration = calibration;
    }

    public ScreenPoint Point(Position position, Side side, Hotspot hotspot)
    {
        switch (hotspot)
        {
            case Hotspot.SelectionBar:
                return SelectionBar(position);
            case Hotspot.CellLeftEdge:
                return CellLeftEdge(position);
            case Hotspot.ColumnTop:
                return ColumnTop(position);
            case Hotspot.RowBar:
                return RowBar(position);
            case Hotspot.TableHandle:
                return TableHandle(position);
        }

        switch (position)
        {
            case InlineShapePosition inlineShape:
                return probe.InlineShapeRectangle(inlineShape.Index).Centre;
            case ShapePosition shape:
                return probe.ShapeRectangle(shape.Index).Centre;
            case BelowPosition below:
            {
                var range = probe.Resolve(below);
                var glyph = probe.GlyphRectangle(range);
                var page = probe.Page();
                var x = side switch
                {
                    Side.Left => page.TextLeft + 20,
                    Side.Right => page.TextRight - 20,
                    _ => (page.TextLeft + page.TextRight) / 2
                };
                return new(x, glyph.Bottom + glyph.Height * 3);
            }
            case TablePosition table:
                return probe.Rectangle(probe.Table(table.Table).Range).Centre;
        }

        var collapsed = probe.Resolve(position);
        var rectangle = probe.GlyphRectangle(collapsed);
        var fraction = side switch
        {
            Side.Left => 0.25,
            Side.Right => 0.75,
            _ => 0.5
        };
        return rectangle.At(fraction, 0.5);
    }

    ScreenPoint SelectionBar(Position position)
    {
        var range = probe.Resolve(position);
        var glyph = probe.GlyphRectangle(range);
        var page = probe.Page();
        var x = page.PageLeft + (page.TextLeft - page.PageLeft) / 2 + calibration.Offsets["selectionBarInset"];
        return new(x, glyph.Top + glyph.Height / 2);
    }

    ScreenPoint CellLeftEdge(Position position)
    {
        var (table, cell) = Cell(position);
        var rectangle = probe.Rectangle(cell.Range);
        var padding = (int) Math.Round(table.LeftPadding * probe.PixelsPerPoint);
        return new(rectangle.Left - padding + calibration.Offsets["cellLeftEdgeInset"], rectangle.Top + rectangle.Height / 2);
    }

    ScreenPoint ColumnTop(Position position)
    {
        var (table, cell) = Cell(position);
        var top = table.Cell(1, cell.ColumnIndex);
        var rectangle = probe.Rectangle(top.Range);
        var leftPadding = (int) Math.Round(table.LeftPadding * probe.PixelsPerPoint);
        var topPadding = (int) Math.Round(table.TopPadding * probe.PixelsPerPoint);
        var width = (int) Math.Round(top.Width * probe.PixelsPerPoint);
        return new(rectangle.Left - leftPadding + width / 2, rectangle.Top - topPadding - calibration.Offsets["columnTopAbove"]);
    }

    ScreenPoint RowBar(Position position)
    {
        var (table, row) = Row(position);
        var first = table.Cell(row, 1);
        var rectangle = probe.Rectangle(first.Range);
        var leftPadding = (int) Math.Round(table.LeftPadding * probe.PixelsPerPoint);
        var tableLeft = rectangle.Left - leftPadding;
        return new(tableLeft - calibration.Offsets["rowBarLeft"], rectangle.Top + rectangle.Height / 2);
    }

    ScreenPoint TableHandle(Position position)
    {
        var tableIndex = position is TablePosition table ? table.Table : 1;
        var wordTable = probe.Table(tableIndex);
        var first = wordTable.Cell(1, 1);
        var rectangle = probe.Rectangle(first.Range);
        var leftPadding = (int) Math.Round(wordTable.LeftPadding * probe.PixelsPerPoint);
        var topPadding = (int) Math.Round(wordTable.TopPadding * probe.PixelsPerPoint);
        var offset = calibration.Offsets["tableHandleOffset"];
        return new(rectangle.Left - leftPadding - offset, rectangle.Top - topPadding - offset);
    }

    public ScreenPoint TableInside(int tableIndex) =>
        probe.Rectangle(probe.Table(tableIndex).Cell(1, 1).Range).Centre;

    (Word.Table Table, Word.Cell Cell) Cell(Position position)
    {
        if (position is CellPosition cellPosition)
        {
            var table = probe.Table(cellPosition.Table);
            return (table, table.Cell(cellPosition.Row, cellPosition.Column));
        }

        if (position is ColumnPosition columnPosition)
        {
            var table = probe.Table(columnPosition.Table);
            return (table, table.Cell(1, columnPosition.Column));
        }

        throw new ArgumentException($"A cell hot spot needs a cell or column position, not {position}.");
    }

    (Word.Table Table, int Row) Row(Position position)
    {
        if (position is RowPosition rowPosition)
        {
            return (probe.Table(rowPosition.Table), rowPosition.Row);
        }

        if (position is CellPosition cellPosition)
        {
            return (probe.Table(cellPosition.Table), cellPosition.Row);
        }

        throw new ArgumentException($"A row hot spot needs a row or cell position, not {position}.");
    }
}
