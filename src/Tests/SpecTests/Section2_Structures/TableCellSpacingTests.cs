/// <summary>
/// Covers <c>w:tblCellSpacing</c> parsing and the detached-border model it triggers.
/// </summary>
public class TableCellSpacingTests
{
    [Test]
    public async Task TableProperties_DefaultCellSpacing_IsZero()
    {
        var props = new TableProperties();

        await Assert.That(props.CellSpacingPoints).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveCellBorders_ZeroCellSpacing_UsesInsideBordersBetweenCells()
    {
        // Sanity check: with no cell spacing, an inner cell uses insideH/insideV — the
        // shared-edge model that was already in place before the detached-border path.
        var cellProps = new TableCellProperties();
        var tableProps = new TableProperties
        {
            DefaultBorders = AllSidesBorder("FF0000"),
            InsideHorizontalBorder = SingleEdge("00FF00"),
            InsideVerticalBorder = SingleEdge("00FF00"),
            CellSpacingPoints = 0
        };

        var borders = TableLayout.ResolveCellBorders(cellProps, tableProps, rowIndex: 1, colIndex: 1, totalRows: 3, totalCols: 3);

        await Assert.That(borders).IsNotNull();
        await Assert.That(borders!.Top.ColorHex).IsEqualTo("00FF00");
        await Assert.That(borders.Left.ColorHex).IsEqualTo("00FF00");
    }

    [Test]
    public async Task ResolveCellBorders_NonZeroCellSpacing_AppliesOuterBorderToAllEdges()
    {
        // Detached-border model: every cell — including inner ones — gets the outer
        // border on all four edges, since the gap means there's no shared edge to share
        // with insideH/insideV.
        var cellProps = new TableCellProperties();
        var tableProps = new TableProperties
        {
            DefaultBorders = AllSidesBorder("FF0000"),
            InsideHorizontalBorder = SingleEdge("00FF00"),
            InsideVerticalBorder = SingleEdge("00FF00"),
            CellSpacingPoints = 1.5
        };

        var borders = TableLayout.ResolveCellBorders(cellProps, tableProps, rowIndex: 1, colIndex: 1, totalRows: 3, totalCols: 3);

        await Assert.That(borders).IsNotNull();
        await Assert.That(borders!.Top.ColorHex).IsEqualTo("FF0000");
        await Assert.That(borders.Bottom.ColorHex).IsEqualTo("FF0000");
        await Assert.That(borders.Left.ColorHex).IsEqualTo("FF0000");
        await Assert.That(borders.Right.ColorHex).IsEqualTo("FF0000");
    }

    [Test]
    public async Task ResolveCellBorders_CellSpacingWithoutOuter_FallsThrough()
    {
        // Spacing alone with no outer border means there's nothing to draw — the cascade
        // falls through to the (null) outer/inside path and returns the all-None result.
        var cellProps = new TableCellProperties();
        var tableProps = new TableProperties { CellSpacingPoints = 2 };

        var borders = TableLayout.ResolveCellBorders(cellProps, tableProps, rowIndex: 0, colIndex: 0, totalRows: 1, totalCols: 1);

        await Assert.That(borders).IsNull();
    }

    [Test]
    public async Task DocumentParser_ParsesTableLevelCellSpacing()
    {
        // Hand-built minimal docx: one table with w:tblCellSpacing="40" type="dxa" (= 2pt).
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "table_cell_spacing", "01", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var table = doc.Elements.OfType<TableElement>().First();

        await Assert.That(table.Properties.CellSpacingPoints).IsEqualTo(2);
    }

    static CellBorders AllSidesBorder(string color) => new()
    {
        Top = new() { IsVisible = true, WidthPoints = 0.5, ColorHex = color },
        Right = new() { IsVisible = true, WidthPoints = 0.5, ColorHex = color },
        Bottom = new() { IsVisible = true, WidthPoints = 0.5, ColorHex = color },
        Left = new() { IsVisible = true, WidthPoints = 0.5, ColorHex = color }
    };

    static BorderEdge SingleEdge(string color) =>
        new() { IsVisible = true, WidthPoints = 0.5, ColorHex = color };
}
