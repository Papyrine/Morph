/// <summary>
/// Covers <c>w:tblPrEx</c> parsing and rendering: row-level overrides of
/// table-wide properties (borders, cell margins).
/// </summary>
public class TablePropertyExceptionsTests
{
    [Test]
    public async Task TableRow_DefaultOverrides_AreNull()
    {
        var row = new TableRow
        {
            Cells = []
        };

        await Assert.That(row.OverrideBorders).IsNull();
        await Assert.That(row.OverrideInsideHBorder).IsNull();
        await Assert.That(row.OverrideInsideVBorder).IsNull();
        await Assert.That(row.OverrideCellPadding).IsNull();
    }

    [Test]
    public async Task ResolveCellBorders_RowOverridesTakePrecedence()
    {
        // Cell with no explicit borders, but row override says "no borders".
        var cellProps = new TableCellProperties();
        var tableProps = new TableProperties
        {
            DefaultBorders = new()
            {
                Top = BorderEdge.Default,
                Bottom = BorderEdge.Default,
                Left = BorderEdge.Default,
                Right = BorderEdge.Default
            }
        };
        var row = new TableRow
        {
            Cells = [],
            OverrideBorders = new()
            {
                Top = BorderEdge.None,
                Bottom = BorderEdge.None,
                Left = BorderEdge.None,
                Right = BorderEdge.None
            }
        };

        var borders = TableLayout.ResolveCellBorders(cellProps, tableProps, rowIndex: 0, colIndex: 0, totalRows: 1, totalCols: 1, row);

        await Assert.That(borders).IsNotNull();
        await Assert.That(borders!.Top.IsVisible).IsFalse();
        await Assert.That(borders.Bottom.IsVisible).IsFalse();
    }

    [Test]
    public async Task GetEffectivePadding_RowOverrideTakesPrecedenceOverTableDefault()
    {
        var cellProps = new TableCellProperties();
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(2, 2, 2, 2)
        };
        var row = new TableRow
        {
            Cells = [],
            OverrideCellPadding = new(5, 5, 5, 5)
        };

        var padding = TableLayout.GetEffectivePadding(cellProps, tableProps, row);

        await Assert.That(padding.Top).IsEqualTo(5);
    }

    [Test]
    public async Task GetEffectivePadding_CellPaddingStillWinsOverRowOverride()
    {
        var cellProps = new TableCellProperties
        {
            Padding = new(10, 10, 10, 10)
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(2, 2, 2, 2)
        };
        var row = new TableRow
        {
            Cells = [],
            OverrideCellPadding = new(5, 5, 5, 5)
        };

        var padding = TableLayout.GetEffectivePadding(cellProps, tableProps, row);

        await Assert.That(padding.Top).IsEqualTo(10);
    }

    /// <summary>
    /// End-to-end: newsletters/04 has rows that override the table's borders to none
    /// via <c>w:tblPrEx</c>. The override must surface on the parsed row.
    /// </summary>
    [Test]
    public async Task DocumentParser_ParsesRowLevelBorderOverrides()
    {
        var inputFile = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "newsletters", "04", "input.docx");

        var parser = new DocumentParser();
        var doc = parser.Parse(inputFile);

        var rowsWithOverride = doc.Elements.OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .Count(_ => _.OverrideBorders != null);

        await Assert.That(rowsWithOverride)
            .IsGreaterThan(0)
            .Because("newsletters/04 has rows with w:tblPrEx/w:tblBorders");
    }
}
