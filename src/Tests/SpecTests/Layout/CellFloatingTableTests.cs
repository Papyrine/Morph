/// <summary>
/// The text of a table cell wraps round a floating table in it (<c>CellFloatWrap</c>), and the row is
/// sized for what that places. Word-probed on <c>_probe_cellfloat</c> (2026-11-19): a one-row 72pt table
/// floated in a 300pt cell under a "TOP" line, 20pt text on exact 24pt lines, read off Word's XPS. Each
/// test here is one of its variants; the row's height comes from <c>TableHeightCalculator</c> and the
/// positions from the Fragmenter, so every row-height check is also a check that the two agree.
/// </summary>
public class CellFloatingTableTests
{
    static readonly PageSettings page = new() { WidthPoints = 400, HeightPoints = 600, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    [Test]
    public async Task With_no_room_beside_it_the_text_goes_under_the_table()
    {
        // Word: AFTERK's line starts at the table's foot.
        var row = Row(P("TOP"), Float(300), P("AFTER"));
        var (top, table, after) = (Line(row, "TOP"), Table(row), Line(row, "AFTER"));

        await Assert.That(table.Y).IsEqualTo(top.Y + top.Height).Within(0.01f);
        await Assert.That(after.Y).IsEqualTo(table.Y + table.Height).Within(0.01f);
        await Assert.That(after.X).IsEqualTo(top.X).Within(0.01f);
        await Assert.That(row.Height).IsEqualTo(24f + 72f + 24f).Within(0.01f);
    }

    [Test]
    public async Task Under_the_table_a_paragraph_keeps_its_space_before()
    {
        // Word: AFTERJ, 24pt before, set 24pt under the table's foot rather than at it.
        var row = Row(P("TOP"), Float(300), P("AFTER", before: 24));
        var (table, after) = (Table(row), Line(row, "AFTER"));

        await Assert.That(after.Y).IsEqualTo(table.Y + table.Height + 24f).Within(0.01f);
        await Assert.That(row.Height).IsEqualTo(24f + 72f + 24f + 24f).Within(0.01f);
    }

    [Test]
    public async Task Beside_a_narrow_table_the_text_sets_in_the_strip_past_its_distance()
    {
        // Word: AFTERH 36pt right of the 100pt table (rightFromText 720), on the table's first line, and
        // the row as tall as the table whatever stands beside it.
        var row = Row(P("TOP"), Float(100, new(0, 36, 0, 0)), P("AFTER"));
        var (top, table, after) = (Line(row, "TOP"), Table(row), Line(row, "AFTER"));

        await Assert.That(after.X).IsEqualTo(top.X + 100f + 36f).Within(0.01f);
        await Assert.That(after.Y).IsEqualTo(table.Y).Within(0.01f);
        await Assert.That(row.Height).IsEqualTo(24f + 72f).Within(0.01f);
    }

    [Test]
    public async Task A_line_that_ends_above_the_table_stays_full_width()
    {
        // Word: with tblpY 36pt, AFTERF sets at the full width straight under TOP, and the row still
        // reaches the table's foot.
        var row = Row(P("TOP"), Float(300, offsetY: 36), P("AFTER"));
        var (top, after) = (Line(row, "TOP"), Line(row, "AFTER"));

        await Assert.That(after.X).IsEqualTo(top.X).Within(0.01f);
        await Assert.That(after.Y).IsEqualTo(top.Y + top.Height).Within(0.01f);
        await Assert.That(row.Height).IsEqualTo(24f + 36f + 72f).Within(0.01f);
    }

    [Test]
    public async Task The_row_holds_the_table_and_its_bottom_distance()
    {
        // Word: a table with only the cell's end mark after it (which collapses) grows the row to its
        // foot, and bottomFromText 720 by 36pt more.
        var row = Row(P("TOP"), Float(300, new(0, 0, 36, 0)), EndMark());

        await Assert.That(row.Height).IsEqualTo(24f + 72f + 36f).Within(0.01f);
    }

    [Test]
    public async Task The_previous_paragraphs_space_after_moves_the_table_down()
    {
        // Word: TOP with 24pt after puts the table 24pt lower.
        var row = Row(P("TOP", after: 24), Float(300), P("AFTER"));
        var (top, table) = (Line(row, "TOP"), Table(row));

        await Assert.That(table.Y).IsEqualTo(top.Y + top.Height + 24f).Within(0.01f);
    }

    [Test]
    public async Task A_strip_too_narrow_to_use_sends_the_text_under_the_table()
    {
        // Not Word's rendering: Word sets even an 8.6pt strip, a letter a line. A strip under 36pt is
        // not used here, as in the page flow (CellFloatWrap).
        var row = Row(P("TOP"), Float(280), P("AFTER"));
        var (top, table, after) = (Line(row, "TOP"), Table(row), Line(row, "AFTER"));

        await Assert.That(after.Y).IsEqualTo(table.Y + table.Height).Within(0.01f);
        await Assert.That(after.X).IsEqualTo(top.X).Within(0.01f);
    }

    static PlacedTableRow Row(params DocumentElement[] content) =>
        new Fragmenter(LayoutTestFonts.Measurer)
            .Layout(
                [
                    new TableElement
                    {
                        Rows =
                        [
                            new()
                            {
                                Cells =
                                [
                                    new()
                                    {
                                        Properties = new() { WidthPoints = 300 },
                                        Content = content
                                    }
                                ]
                            }
                        ],
                        Properties = new() { GridColumnWidths = [300], IsAutoFit = false }
                    }
                ],
                page)
            .Pages[0]
            .Items
            .OfType<PlacedTableRow>()
            .Single();

    static PlacedLine Line(PlacedTableRow row, string text) =>
        row.Cells[0].Content.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == text));

    static PlacedTableRow Table(PlacedTableRow row) =>
        row.Cells[0].Content.OfType<PlacedTableRow>().Single();

    // A floating table anchored to the text both ways: one row, as tall as its one 72pt line.
    static TableElement Float(double width, CellSpacing? distance = null, double offsetY = 0) => new()
    {
        Rows =
        [
            new()
            {
                Cells =
                [
                    new()
                    {
                        Properties = new() { WidthPoints = width },
                        Content = [P("F", lineHeight: 72)]
                    }
                ]
            }
        ],
        Properties = new()
        {
            GridColumnWidths = [width],
            IsAutoFit = false,
            IsFloating = true,
            FloatingVerticalAnchor = FloatingTableVerticalAnchor.Text,
            FloatingHorizontalAnchor = FloatingTableHorizontalAnchor.Text,
            FloatingYOffsetPoints = offsetY,
            FloatingDistance = distance ?? new()
        }
    };

    static ParagraphElement P(string text, double before = 0, double after = 0, double lineHeight = 24) => new()
    {
        Runs = [new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 20 } }],
        Properties = Exact(before, after, lineHeight)
    };

    // The empty paragraph a cell must end with, collapsed after a table as the parser marks it.
    static ParagraphElement EndMark() => new()
    {
        Runs = [],
        Properties = Exact(0, 0, 24),
        IsCollapsedCellMark = true
    };

    static ParagraphProperties Exact(double before, double after, double lineHeight) => new()
    {
        SpacingBeforePoints = before,
        SpacingAfterPoints = after,
        LineSpacingRule = LineSpacingRule.Exactly,
        LineSpacingPoints = lineHeight
    };
}
