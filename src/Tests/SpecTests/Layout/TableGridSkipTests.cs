/// <summary>
/// <c>w:gridBefore</c> / <c>w:gridAfter</c> — grid columns a row leaves empty. Word-probed in
/// <c>table_grid_before_after</c> (see its notes.md): the row's first cell starts at grid column
/// <c>gridBefore</c>, the cells keep their grid widths, and nothing is drawn in the skipped columns.
/// </summary>
public class TableGridSkipTests
{
    static readonly PageSettings page = new() { WidthPoints = 600, HeightPoints = 600, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    static string Fixture => Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "table_grid_before_after", "input.docx");

    [Test]
    public async Task The_parser_reads_the_skips_of_each_row()
    {
        var table = new DocumentParser().Parse(Fixture).Elements.OfType<TableElement>().First();

        await Assert.That(table.Rows.Select(_ => (_.GridBefore, _.GridAfter)))
            .IsEquivalentTo([(0, 0), (1, 0), (0, 2), (1, 1), (2, 0), (1, 0)]);
    }

    [Test]
    public async Task A_rows_first_cell_starts_at_its_gridBefore_column()
    {
        var rows = Rows(Table(Row(0, 0, "A", "B", "C", "D"), Row(1, 0, "B", "C", "D")));
        var full = rows[0].Cells;
        var skipped = rows[1].Cells;

        await Assert.That(skipped.Select(_ => _.X)).IsEquivalentTo(full.Skip(1).Select(_ => _.X));
        await Assert.That(skipped.Select(_ => _.Width)).IsEquivalentTo(full.Skip(1).Select(_ => _.Width));
    }

    [Test]
    public async Task A_row_that_stops_short_keeps_its_cells_in_their_columns()
    {
        var rows = Rows(Table(Row(0, 0, "A", "B", "C", "D"), Row(0, 2, "A", "B")));

        await Assert.That(rows[1].Cells.Select(_ => (_.X, _.Width))).IsEquivalentTo(rows[0].Cells.Take(2).Select(_ => (_.X, _.Width)));
    }

    [Test]
    public async Task A_row_skipping_at_both_ends_sits_in_the_middle_columns()
    {
        var rows = Rows(Table(Row(0, 0, "A", "B", "C", "D"), Row(1, 1, "B", "C")));

        await Assert.That(rows[1].Cells.Select(_ => (_.X, _.Width))).IsEquivalentTo(rows[0].Cells.Skip(1).Take(2).Select(_ => (_.X, _.Width)));
    }

    static List<PlacedTableRow> Rows(TableElement table) =>
        new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page).Pages[0].Items.OfType<PlacedTableRow>().ToList();

    // A borderless table over a grid of unequal columns, so a cell in the wrong column has the wrong width.
    static TableElement Table(params List<TableRow> rows) => new()
    {
        Properties = new() { GridColumnWidths = [72, 108, 72, 144] },
        Rows = rows
    };

    static TableRow Row(int before, int after, params string[] texts) =>
        new()
        {
            GridBefore = before,
            GridAfter = after,
            HeightPoints = 30,
            Cells = texts.Select(_ => new TableCell { Content = [Paragraph(_)] }).ToList()
        };

    static ParagraphElement Paragraph(string text) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }]
    };
}
