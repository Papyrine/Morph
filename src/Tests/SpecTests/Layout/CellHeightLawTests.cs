/// <summary>
/// How a row's declared height, its cells' top and bottom margins and a nested table add up to the row's
/// box. Each law is XPS-read from Word on <c>_probe_cellheights</c> / <c>_probe_exact12</c> /
/// <c>_probe_exact15</c> at two or more magnitudes (<c>docs/word-features.md</c>, Row Height and Nested
/// Tables), and together they take resumes/06 from four pages to Word's three.
/// </summary>
public class CellHeightLawTests
{
    static readonly PageSettings page = new() { WidthPoints = 400, HeightPoints = 600, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task An_at_least_floor_is_the_content_box_with_both_margins_outside_it()
    {
        var table = Table([Row([Cell([Paragraph("one")], new(40, 0, 20, 0))], 60, exact: false)], [260]);

        var row = Rows(table).Single();

        await Assert.That(row.Height).IsEqualTo(40 + 60 + 20).Within(0.01f);
    }

    [Test]
    public async Task An_exact_height_holds_the_top_margin_and_adds_the_bottom_one()
    {
        var table = Table([Row([Cell([Paragraph("one")], new(40, 0, 20, 0))], 60, exact: true)], [260]);

        var row = Rows(table).Single();
        var line = row.Cells[0].Content.OfType<PlacedLine>().Single();

        await Assert.That(row.Height).IsEqualTo(60 + 20).Within(0.01f);
        await Assert.That(line.Y).IsEqualTo(row.Y + 40).Within(0.01f);
    }

    [Test]
    public async Task Every_cell_takes_the_largest_top_and_bottom_margin_in_its_row()
    {
        var table = Table(
            [
                Row(
                [
                    Cell([Paragraph("A")], new(40, 0, 0, 0)),
                    Cell([Paragraph("B")], null),
                    Cell([Paragraph("C")], new(0, 0, 30, 0)),
                    Cell([Paragraph("D")], null, CellVerticalAlignment.Bottom)
                ])
            ],
            [60, 60, 60, 60]);

        var row = Rows(table).Single();
        var lines = row.Cells.Select(_ => _.Content.OfType<PlacedLine>().Single()).ToList();
        var lineHeight = lines[0].Height;

        await Assert.That(row.Height).IsEqualTo(40 + lineHeight + 30).Within(0.01f);
        foreach (var line in lines)
        {
            await Assert.That(line.Y).IsEqualTo(row.Y + 40).Within(0.01f);
        }
    }

    [Test]
    public async Task A_nested_table_is_measured_and_its_trailing_mark_takes_nothing()
    {
        var table = Table([Row([Cell([Nested(), CollapsedMark()], null)])], [260]);

        var row = Rows(table).Single();

        await Assert.That(row.Height).IsEqualTo(30).Within(0.01f);
    }

    [Test]
    public async Task A_centred_cell_centres_its_nested_table()
    {
        var table = Table([Row([Cell([Nested(), CollapsedMark()], null, CellVerticalAlignment.Center)], 60, exact: false)], [260]);

        var row = Rows(table).Single();
        var nested = row.Cells[0].Content.OfType<PlacedTableRow>().Single();

        await Assert.That(row.Height).IsEqualTo(60).Within(0.01f);
        await Assert.That(nested.Y).IsEqualTo(row.Y + 15).Within(0.01f);
    }

    [Test]
    public async Task A_cells_own_margin_and_top_edge_stack()
    {
        var table = Table(
            [
                Row([Cell([Paragraph("a1")], null), Cell([Paragraph("b1")], null)]),
                Row([Cell([Paragraph("a2")], new(4, 0, 0, 0), top: 6), Cell([Paragraph("b2")], new(4, 0, 0, 0), top: 6)])
            ],
            [130, 130]);

        var rows = Rows(table);
        var line = rows[1].Cells[1].Content.OfType<PlacedLine>().Single();

        await Assert.That(line.Y - rows[1].Y).IsEqualTo(4 + 6).Within(0.01f);
    }

    [Test]
    public async Task A_margin_on_one_cell_and_an_edge_on_another_give_the_larger()
    {
        var table = Table(
            [
                Row([Cell([Paragraph("a1")], null), Cell([Paragraph("b1")], null)]),
                Row([Cell([Paragraph("a2")], new(10, 0, 0, 0)), Cell([Paragraph("b2")], null, top: 6)]),
                Row([Cell([Paragraph("a3")], new(4, 0, 0, 0)), Cell([Paragraph("b3")], null, top: 12)])
            ],
            [130, 130]);

        var rows = Rows(table);
        var second = rows[1].Cells[1].Content.OfType<PlacedLine>().Single();
        var third = rows[2].Cells[1].Content.OfType<PlacedLine>().Single();

        await Assert.That(second.Y - rows[1].Y).IsEqualTo(10).Within(0.01f);
        await Assert.That(third.Y - rows[2].Y).IsEqualTo(12).Within(0.01f);
    }

    [Test]
    public async Task A_merged_cell_holds_its_content_across_the_edges_between_its_rows()
    {
        // A two-row merge beside a cell whose 6pt bottom edge closes the first row: the merged content
        // needs 50pt, and the edge's 6pt counts towards it rather than going on top.
        var table = Table(
            [
                Row([Cell([Nested(height: 50), CollapsedMark()], null, merge: VerticalMergeType.Restart), Cell([Paragraph("b1")], null, bottom: 6)], 20, exact: false),
                Row([Cell([], null, merge: VerticalMergeType.Continue), Cell([Paragraph("b2")], null)], 10, exact: false)
            ],
            [130, 130]);

        var rows = Rows(table);

        await Assert.That(rows.Sum(_ => _.Height)).IsEqualTo(50).Within(0.01f);
    }

    static List<PlacedTableRow> Rows(TableElement table) =>
        new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page).Pages[0].Items.OfType<PlacedTableRow>().ToList();

    static TableElement Table(List<TableRow> rows, List<double> widths) =>
        new() { Rows = rows, Properties = new() { GridColumnWidths = widths } };

    static TableRow Row(List<TableCell> cells, double? height = null, bool exact = false) =>
        new() { HeightPoints = height, IsExactHeight = exact, Cells = cells };

    static TableCell Cell(List<DocumentElement> content, CellSpacing? padding, CellVerticalAlignment alignment = CellVerticalAlignment.Top, double top = 0, double bottom = 0, VerticalMergeType merge = VerticalMergeType.None) =>
        new() { Properties = new() { Padding = padding, VerticalAlignment = alignment, Borders = Edges(top, bottom), VerticalMerge = merge }, Content = content };

    // A w:tcBorders declaring only the given top and bottom edges, or none.
    static CellBorders? Edges(double top, double bottom)
    {
        if (top == 0 && bottom == 0)
        {
            return null;
        }

        return new()
        {
            Top = top > 0 ? new() { IsVisible = true, WidthPoints = top } : BorderEdge.None,
            Bottom = bottom > 0 ? new() { IsVisible = true, WidthPoints = bottom } : BorderEdge.None,
            Declared = (top > 0 ? BorderSides.Top : 0) | (bottom > 0 ? BorderSides.Bottom : 0)
        };
    }

    // A one-row exact table, 30pt unless told otherwise, as a cell's nested content.
    static TableElement Nested(double height = 30) =>
        Table([Row([Cell([Paragraph("inner")], null)], height, exact: true)], [100]);

    // The empty end-of-cell mark after a nested table, given spacing and a size that would cost the row
    // well over its 30pt if any of it counted.
    static ParagraphElement CollapsedMark() => new()
    {
        Runs = [],
        IsCollapsedCellMark = true,
        Properties = new() { SpacingBeforePoints = 30, SpacingAfterPoints = 40, LineSpacingMultiplier = 1 }
    };

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
        Properties = new() { SpacingAfterPoints = 0, LineSpacingMultiplier = 1 }
    };
}
