/// <summary>
/// Tests the block-flow slice of the <see cref="Fragmenter"/> (step 3 of
/// <c>docs/layout-engine-proposal.md</c>): single-column pagination with line-level page breaks and the
/// height-model spacing rules. A small page geometry forces the interesting boundaries.
/// </summary>
public class CanonicalFragmenterTests
{
    static readonly Fragmenter Fragmenter = new(LayoutTestFonts.Measurer);

    // 300pt wide, 20pt margins → 260pt measure. At 200pt tall the content band is 160pt = 11 Aptos-11
    // lines (14.5pt each), so the twelfth line breaks the page.
    static PageSettings Page(double heightPoints) =>
        new() { WidthPoints = 300, HeightPoints = heightPoints, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };

    // The same geometry with N equal columns at a 20pt gap. At 2 columns the 260pt measure splits into
    // 120pt columns; column 1's left edge sits at 20 + 120 + 20 = 160pt.
    static PageSettings ColumnPage(double heightPoints, int columns) =>
        new() { WidthPoints = 300, HeightPoints = heightPoints, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20, ColumnCount = columns, ColumnSpacing = 20 };

    static ParagraphElement P(string text, ParagraphProperties? properties = null) =>
        new()
        {
            Runs = [new Run { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
            Properties = properties ?? new()
        };

    [Test]
    public async Task Short_paragraphs_fit_on_one_page()
    {
        var document = Fragmenter.Layout([P("One"), P("Two"), P("Three")], Page(200));
        await Assert.That(document.Pages.Count).IsEqualTo(1);
        await Assert.That(document.Pages[0].Items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_tall_paragraph_splits_across_pages_at_line_boundaries()
    {
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 220)));
        var page = Page(200);
        var totalLines = LayoutTestFonts.Measurer.LayoutLines(paragraph, (float) page.ContentWidth).Count;

        var document = Fragmenter.Layout([paragraph], page);

        // Every wrapped line is placed exactly once, and the paragraph continues onto a second page —
        // the line-level split the raster backends cannot do.
        await Assert.That(document.Pages.Sum(_ => _.Items.Count)).IsEqualTo(totalLines);
        await Assert.That(document.Pages.Count > 1).IsTrue();
        await Assert.That(ReferenceEquals(((PlacedLine) document.Pages[1].Items[0]).Paragraph, paragraph)).IsTrue();

        // No placed line overflows the content bottom (180pt here).
        foreach (var placed in document.Pages.SelectMany(_ => _.Items))
        {
            await Assert.That(placed.Y + placed.Height <= 180.01f).IsTrue();
        }
    }

    [Test]
    public async Task Space_before_is_dropped_at_a_broken_page_top()
    {
        // Eleven single-line paragraphs fill page 1; a twelfth with a big space-before lands atop page 2.
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();
        var moved = P("moved", new ParagraphProperties { SpacingBeforePoints = 50 });

        var document = Fragmenter.Layout([.. fillers, moved], Page(200));

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // Its first line sits at the content top — the 50pt before was dropped, not applied.
        await Assert.That(document.Pages[1].Items[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task Page_break_element_starts_a_new_page()
    {
        var document = Fragmenter.Layout([P("before"), new PageBreakElement(), P("after")], Page(400));
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[1].Items[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task Empty_document_is_one_empty_page()
    {
        var document = Fragmenter.Layout([], Page(200));
        await Assert.That(document.Pages.Count).IsEqualTo(1);
        await Assert.That(document.Pages[0].Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Content_overflowing_a_column_flows_to_the_next_column_on_the_same_page()
    {
        // A paragraph taller than one 160pt column fills column 0, then column 1, all on page 1.
        var page = ColumnPage(200, 2);
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 300)));
        var column1Left = 20f + (float) page.ColumnWidth + 20f;

        var document = Fragmenter.Layout([paragraph], page);
        var firstPageLines = document.Pages[0].Items.Cast<PlacedLine>().ToList();

        // Both columns are used on page 1, and column 1 resumes at the content top (20pt), not column 0's
        // running y — the region-top reset the raster backends get from MoveToNextColumn.
        await Assert.That(firstPageLines.Any(_ => Math.Abs(_.X - 20f) < 0.5f)).IsTrue();
        var column1Lines = firstPageLines.Where(_ => Math.Abs(_.X - column1Left) < 0.5f).ToList();
        await Assert.That(column1Lines.Count > 0).IsTrue();
        await Assert.That(column1Lines[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task A_column_break_moves_to_the_next_column()
    {
        var page = ColumnPage(400, 2);
        var column1Left = 20f + (float) page.ColumnWidth + 20f;

        var document = Fragmenter.Layout([P("before"), new ColumnBreakElement(), P("after")], page);

        // One page: "before" in column 0, "after" atop column 1 — the break advances the column, not the page.
        await Assert.That(document.Pages.Count).IsEqualTo(1);
        var lines = document.Pages[0].Items.Cast<PlacedLine>().ToList();
        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0].X).IsEqualTo(20f).Within(0.5f);
        await Assert.That(lines[1].X).IsEqualTo(column1Left).Within(0.5f);
        await Assert.That(lines[1].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task Overflowing_the_last_column_starts_a_new_page_at_column_zero()
    {
        // Enough text to fill both columns of page 1 and spill onto page 2.
        var document = Fragmenter.Layout([P(string.Join(' ', Enumerable.Repeat("lorem", 800)))], ColumnPage(200, 2));

        await Assert.That(document.Pages.Count > 1).IsTrue();
        // Page 2 resumes at column 0 (x=20), content top.
        var page2First = (PlacedLine) document.Pages[1].Items[0];
        await Assert.That(page2First.X).IsEqualTo(20f).Within(0.5f);
        await Assert.That(page2First.Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task A_uniform_line_is_one_run_carrying_the_whole_text()
    {
        var line = (PlacedLine) Fragmenter.Layout([P("hello world")], Page(400)).Pages[0].Items[0];

        await Assert.That(line.Runs.Count).IsEqualTo(1);
        await Assert.That(line.Runs[0].Text).IsEqualTo("hello world");
        await Assert.That(line.Runs[0].X).IsEqualTo(line.X).Within(0.01f);
    }

    [Test]
    public async Task A_mixed_format_line_splits_into_a_run_per_source_run()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new Run { Text = "plain ", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } },
                new Run { Text = "bold", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11, Bold = true } }
            ],
            Properties = new()
        };

        var line = (PlacedLine) Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];

        // One run per source run, each with its own formatting, placed left to right.
        await Assert.That(line.Runs.Count).IsEqualTo(2);
        await Assert.That(line.Runs[0].Text).IsEqualTo("plain ");
        await Assert.That(line.Runs[0].Properties.Bold).IsFalse();
        await Assert.That(line.Runs[1].Text).IsEqualTo("bold");
        await Assert.That(line.Runs[1].Properties.Bold).IsTrue();
        await Assert.That(line.Runs[0].X).IsEqualTo(line.X).Within(0.01f);
        await Assert.That(line.Runs[1].X > line.Runs[0].X).IsTrue();
    }

    [Test]
    public async Task A_table_lays_out_cells_with_their_content_tiled_left_to_right()
    {
        var table = new TableElement
        {
            Properties = new() { GridColumnWidths = [120, 120] },
            Rows =
            [
                new TableRow
                {
                    Cells =
                    [
                        new TableCell { Content = [P("left cell")], Properties = new() },
                        new TableCell { Content = [P("right cell")], Properties = new() }
                    ]
                }
            ]
        };

        var row = Fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();

        // Two cells tiling left to right, each carrying its paragraph text inside its box.
        await Assert.That(row.Cells.Count).IsEqualTo(2);
        await Assert.That(row.Cells[1].X).IsEqualTo(row.Cells[0].X + row.Cells[0].Width).Within(0.5f);
        await Assert.That(row.Cells[0].Content.OfType<PlacedLine>().SelectMany(_ => _.Runs).Single().Text).IsEqualTo("left cell");
        await Assert.That(row.Cells[1].Content.OfType<PlacedLine>().SelectMany(_ => _.Runs).Single().Text).IsEqualTo("right cell");

        // Content is inset within the cell box (padding on the left, below the top).
        var leftLine = row.Cells[0].Content.OfType<PlacedLine>().First();
        await Assert.That(leftLine.X >= row.Cells[0].X).IsTrue();
        await Assert.That(leftLine.Y >= row.Cells[0].Y).IsTrue();
    }

    [Test]
    public async Task A_list_paragraph_places_its_marker_in_the_hanging_indent()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new Run { Text = "list item text", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
            Properties = new()
            {
                LeftIndentPoints = 36,
                HangingIndentPoints = 18,
                Numbering = new NumberingInfo { Text = "1." }
            }
        };

        var line = (PlacedLine) Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];

        // The first run is the marker, a hanging indent (18pt) left of the text edge (line.X = 20 + 36).
        await Assert.That(line.Runs[0].Text).IsEqualTo("1.");
        await Assert.That(line.Runs[0].X).IsEqualTo(line.X - 18f).Within(0.01f);
        // The text run follows at the line's left edge.
        await Assert.That(line.Runs[1].Text).IsEqualTo("list item text");
        await Assert.That(line.Runs[1].X).IsEqualTo(line.X).Within(0.01f);
    }
}
