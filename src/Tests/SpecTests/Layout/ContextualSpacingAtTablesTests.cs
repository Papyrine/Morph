/// <summary>
/// Contextual spacing across a table's boundaries: the next paragraph in the document is the one a
/// paragraph's space-after is weighed against, wherever it stands. Word-probed 2026-10-11
/// (<c>_probe_cellctx</c>: one-row tables of 10pt Arial, 24pt after, the row's rules read against
/// the baselines either side of them).
/// </summary>
public class ContextualSpacingAtTablesTests
{
    // "S" is the default style with contextual spacing, "P" the same without it, "H" a contextual
    // paragraph in another style, "T" a plain one in that other style.
    static ParagraphElement Paragraph(string kind) =>
        new()
        {
            Runs = [new() {Text = kind, Properties = new() {FontFamily = "Arial", FontSizePoints = 10}}],
            Properties = new()
            {
                SpacingAfterPoints = kind == "T" ? 0 : 24,
                LineSpacingMultiplier = 1,
                ContextualSpacing = kind is "S" or "H",
                StyleId = kind is "H" or "T" ? "Heading1" : null,
                ResolvedStyleId = kind is "H" or "T" ? "Heading1" : "Normal",
                InDefaultStyle = kind is "S" or "P"
            }
        };

    static TableCell Cell(params string[] kinds) =>
        new() {Content = [.. kinds.Select(Paragraph)]};

    static TableRow Row(params TableCell[] cells) =>
        new() {Cells = cells};

    // The probe's cases: what the cell's last paragraph kept of its 24pt, read as the distance from
    // its baseline to the row's bottom rule (2.3pt with nothing kept, 26.3 with all of it).
    [Test]
    public async Task A_contextual_paragraph_ending_the_rows_last_cell_drops_its_after_in_the_default_style()
    {
        var only = Cell("S", "S");
        await Assert.That(ParagraphGap.DropsCellEndAfter(Row(only), only)).IsTrue();
    }

    [Test]
    public async Task Without_the_flag_it_keeps_it()
    {
        var only = Cell("P", "P");
        await Assert.That(ParagraphGap.DropsCellEndAfter(Row(only), only)).IsFalse();
    }

    [Test]
    public async Task In_another_style_it_keeps_it_against_the_rows_end_mark()
    {
        var only = Cell("H", "H");
        await Assert.That(ParagraphGap.DropsCellEndAfter(Row(only), only)).IsFalse();
    }

    [Test]
    public async Task In_an_earlier_cell_it_is_weighed_against_the_next_cells_first_paragraph()
    {
        var first = Cell("S", "S");
        await Assert.That(ParagraphGap.DropsCellEndAfter(Row(first, Cell("S")), first)).IsTrue();
        await Assert.That(ParagraphGap.DropsCellEndAfter(Row(first, Cell("T")), first)).IsFalse();
    }

    // The row is sized without what was dropped.
    [Test]
    public async Task The_row_is_measured_without_the_dropped_after()
    {
        static float Height(string kind)
        {
            var cell = Cell(kind, kind);
            var row = Row(cell);
            return TableHeightCalculator.MeasureCellHeight(cell, 200, new(), LayoutTestFonts.Measurer, row);
        }

        await Assert.That(Height("P") - Height("S")).IsEqualTo(48f).Within(0.01f);
        await Assert.That(Height("H") - Height("S")).IsEqualTo(24f).Within(0.01f);
    }

    // Ahead of a table the next paragraph is the table's first: 24pt after left the top rule 2.6pt
    // under the baseline when the first cell opened in the paragraph's style.
    [Test]
    public async Task A_contextual_paragraph_drops_its_after_ahead_of_a_table_that_opens_in_its_style()
    {
        var same = new TableElement {Rows = [Row(Cell("S"))], Properties = new()};
        var other = new TableElement {Rows = [Row(Cell("T"))], Properties = new()};

        await Assert.That(ParagraphGap.BeforeTable(24, true, "Normal", true, same)).IsEqualTo(0);
        await Assert.That(ParagraphGap.BeforeTable(24, true, "Normal", true, other)).IsEqualTo(24);
        await Assert.That(ParagraphGap.BeforeTable(24, false, "Normal", true, same)).IsEqualTo(24);
    }

    // letters/01: the header table floats under a contextual paragraph of its first cell's style and
    // ends on one. Word's "Date" baseline is 12.6pt under the baseline of the empty line above the
    // table, and the line after the table one 14.4pt pitch under the cell's last.
    [Test]
    public async Task Letters_01s_header_table_stands_where_Word_has_it()
    {
        var path = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "letters", "01", "input.docx");
        await using var stream = File.OpenRead(path);
        var document = new DocumentParser().Parse(stream);
        var page = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(document.Elements, document.PageSettings).Pages[0];

        var row = page.Items.OfType<PlacedTableRow>().First();
        var before = page.Items.OfType<PlacedLine>().Last(_ => _.Y < row.Y);
        var after = page.Items.OfType<PlacedLine>().First(_ => _.Y >= row.Y + row.Height - 0.01f);
        var cellLines = row.Cells.SelectMany(_ => _.Content).OfType<PlacedLine>().ToList();
        var date = cellLines.First(_ => _.Runs.Any(run => run.Text == "Date"));
        var last = cellLines.OrderBy(_ => _.Baseline).Last();

        await Assert.That(date.Baseline - before.Baseline).IsEqualTo(12.6f).Within(0.5f);
        await Assert.That(after.Baseline - last.Baseline).IsEqualTo(14.4f).Within(0.2f);
    }
}
