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

    static List<PlacedTableRow> Rows(TableElement table) =>
        new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page).Pages[0].Items.OfType<PlacedTableRow>().ToList();

    static TableElement Table(List<TableRow> rows, List<double> widths) =>
        new() { Rows = rows, Properties = new() { GridColumnWidths = widths } };

    static TableRow Row(List<TableCell> cells, double? height = null, bool exact = false) =>
        new() { HeightPoints = height, IsExactHeight = exact, Cells = cells };

    static TableCell Cell(List<DocumentElement> content, CellSpacing? padding, CellVerticalAlignment alignment = CellVerticalAlignment.Top) =>
        new() { Properties = new() { Padding = padding, VerticalAlignment = alignment }, Content = content };

    // A one-row 30pt exact table, as a cell's nested content.
    static TableElement Nested() =>
        Table([Row([Cell([Paragraph("inner")], null)], 30, exact: true)], [200]);

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
