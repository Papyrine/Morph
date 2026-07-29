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
    public async Task An_empty_paragraphs_after_spacing_shifts_the_following_paragraph_down()
    {
        ParagraphElement Empty(double after) =>
            new()
            {
                Runs = [new Run { Text = "", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
                Properties = new() { SpacingAfterPoints = after }
            };

        float BeeY(LaidOutDocument document) =>
            document.Pages[0].Items.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "B")).Y;

        var withAfter = BeeY(Fragmenter.Layout([P("A"), Empty(10), P("B")], Page(400)));
        var withoutAfter = BeeY(Fragmenter.Layout([P("A"), Empty(0), P("B")], Page(400)));

        // An empty spacer paragraph carries its after-spacing into the gap before B (max-collapse with B's
        // zero before-spacing), so B sits 10pt lower than when the spacer has no after-spacing. Word applies
        // an empty paragraph's after-spacing like any other's — it is not a bare mark line.
        await Assert.That(withAfter - withoutAfter).IsEqualTo(10f).Within(0.5f);
    }

    [Test]
    public async Task A_line_whose_baseline_clears_the_bottom_margin_stays_on_the_page()
    {
        var probe = Fragmenter.Layout([P("probe")], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
        var lineHeight = probe.Height;
        var ascent = probe.Baseline - probe.Y;

        // A content band that ends between the third line's baseline and its bottom: the third line's
        // descent must spill past the margin. Word keeps it on the page; the fragmenter mirrors that.
        var page = Page(40 + 2 * lineHeight + ascent + 0.5);

        var document = Fragmenter.Layout([P("one"), P("two"), P("three")], page);
        var lines = document.Pages[0].Items.OfType<PlacedLine>().ToList();

        await Assert.That(document.Pages.Count).IsEqualTo(1);
        await Assert.That(lines.Count).IsEqualTo(3);
        // The third line's bottom genuinely exceeds the content bottom — the tolerance is exercised, not a
        // case that would have fit anyway.
        await Assert.That(lines[2].Y + lines[2].Height > page.HeightPoints - page.MarginBottom).IsTrue();
    }

    [Test]
    public async Task Character_spacing_widens_a_runs_measured_width()
    {
        float Width(double tracking) =>
            Fragmenter.Layout(
                    [new ParagraphElement { Runs = [new Run { Text = "ACCOUNTANT", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11, CharacterSpacingPoints = tracking } }] }],
                    Page(400))
                .Pages[0].Items.OfType<PlacedLine>().Single().Width;

        // w:spacing tracking adds its points to every one of the 10 characters' advances, so the line is
        // ~20pt wider at 2pt tracking. Without it entering the width, wrap and alignment would be wrong.
        await Assert.That(Width(2) - Width(0)).IsEqualTo(20f).Within(1f);
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
        // Each run has a positive width, and the runs tile — the second starts where the first ends.
        await Assert.That(line.Runs[0].Width > 0).IsTrue();
        await Assert.That(line.Runs[1].X).IsEqualTo(line.Runs[0].X + line.Runs[0].Width).Within(0.01f);
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
    public async Task A_cell_applies_its_first_paragraphs_space_before()
    {
        // TableHeightCalculator sizes a cell with its first paragraph's space-before, so the content must
        // be positioned with it too (a page-flow paragraph drops it at a region top; a cell does not).
        TableElement OneCell(double before) =>
            new()
            {
                Properties = new() { GridColumnWidths = [200] },
                Rows = [new TableRow { Cells = [new TableCell { Content = [P("cell text", new() { SpacingBeforePoints = before })], Properties = new() }] }]
            };

        var withoutBefore = Fragmenter.Layout([OneCell(0)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();
        var withBefore = Fragmenter.Layout([OneCell(20)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();

        var withoutY = withoutBefore.Cells[0].Content.OfType<PlacedLine>().First().Y;
        var withY = withBefore.Cells[0].Content.OfType<PlacedLine>().First().Y;
        await Assert.That(withY - withoutY).IsEqualTo(20f).Within(0.5f);
    }

    [Test]
    public async Task A_bottom_aligned_cell_shifts_its_content_below_a_top_aligned_one()
    {
        // A tall neighbour forces a tall row; the short cell's line then sits far lower when bottom-aligned.
        TableElement TwoCell(CellVerticalAlignment align) =>
            new()
            {
                Properties = new() { GridColumnWidths = [120, 120] },
                Rows =
                [
                    new TableRow
                    {
                        Cells =
                        [
                            new TableCell { Content = [P(string.Join(' ', Enumerable.Repeat("lorem", 60)))], Properties = new() },
                            new TableCell { Content = [P("short")], Properties = new() { VerticalAlignment = align } }
                        ]
                    }
                ]
            };

        var topAligned = Fragmenter.Layout([TwoCell(CellVerticalAlignment.Top)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();
        var bottomAligned = Fragmenter.Layout([TwoCell(CellVerticalAlignment.Bottom)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();

        var topLineY = topAligned.Cells[1].Content.OfType<PlacedLine>().Single().Y;
        var bottomLineY = bottomAligned.Cells[1].Content.OfType<PlacedLine>().Single().Y;
        await Assert.That(bottomLineY > topLineY + 30f).IsTrue();
    }

    [Test]
    public async Task A_behind_text_cell_float_shape_is_placed_before_the_cells_content_at_a_cell_relative_offset()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 80,
            HeightPoints = 40,
            HorizontalPositionPoints = 6,
            VerticalPositionPoints = 8,
            BehindText = true,
            FillColorHex = "0F3344",
            Preset = PresetShape.Rect
        };
        var table = new TableElement
        {
            Properties = new() { GridColumnWidths = [200] },
            Rows =
            [
                new TableRow
                {
                    Cells = [new TableCell { Content = [P("recipient")], Floats = [shape], Properties = new() }]
                }
            ]
        };

        var cell = Fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];

        // The shape is emitted ahead of the text line so a painter draws it behind the content.
        var contentList = cell.Content.ToList();
        var placed = contentList.OfType<PlacedShape>().Single();
        await Assert.That(contentList.FindIndex(_ => _ is PlacedShape) < contentList.FindIndex(_ => _ is PlacedLine)).IsTrue();
        await Assert.That(placed.Shape.FillColorHex).IsEqualTo("0F3344");
        // Offset is measured from the cell's top-left, not the page.
        await Assert.That(placed.X).IsEqualTo(cell.X + 6f).Within(0.01f);
        await Assert.That(placed.Y).IsEqualTo(cell.Y + 8f).Within(0.01f);
        await Assert.That(placed.Width).IsEqualTo(80f).Within(0.01f);
    }

    [Test]
    public async Task An_in_front_of_text_cell_float_shape_is_not_placed()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 80,
            HeightPoints = 40,
            BehindText = false,
            FillColorHex = "0F3344"
        };
        var table = new TableElement
        {
            Properties = new() { GridColumnWidths = [200] },
            Rows = [new TableRow { Cells = [new TableCell { Content = [P("recipient")], Floats = [shape], Properties = new() }] }]
        };

        var cell = Fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];
        await Assert.That(cell.Content.OfType<PlacedShape>().Any()).IsFalse();
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

    [Test]
    public async Task An_inline_image_places_with_its_bottom_on_the_baseline_and_grows_the_line()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new Run { Text = "", InlineImageData = [1, 2, 3], InlineImageWidthPoints = 100, InlineImageHeightPoints = 80, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
            Properties = new()
        };

        var line = (PlacedLine) Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];

        // One placed image at the line's left edge, its box the image's display size.
        await Assert.That(line.Images.Count).IsEqualTo(1);
        var image = line.Images[0];
        await Assert.That(image.X).IsEqualTo(line.X).Within(0.5f);
        await Assert.That(image.Width).IsEqualTo(100f).Within(0.5f);
        await Assert.That(image.Height).IsEqualTo(80f).Within(0.5f);
        // Its bottom sits on the baseline, and the 80pt image grows the line past an 11pt text line.
        await Assert.That(image.Y + image.Height).IsEqualTo(line.Baseline).Within(0.5f);
        await Assert.That(line.Height >= 80f).IsTrue();
    }

    [Test]
    public async Task Centre_and_right_alignment_shift_the_line_and_its_runs()
    {
        // 300pt wide, 20pt margins → 260pt available. A short word centres to half the slack and
        // right-aligns flush to the right content edge (280pt).
        var left = (PlacedLine) Fragmenter.Layout([P("word")], Page(400)).Pages[0].Items[0];
        var centred = (PlacedLine) Fragmenter.Layout([P("word", new() { Alignment = TextAlignment.Center })], Page(400)).Pages[0].Items[0];
        var right = (PlacedLine) Fragmenter.Layout([P("word", new() { Alignment = TextAlignment.Right })], Page(400)).Pages[0].Items[0];

        await Assert.That(left.X).IsEqualTo(20f).Within(0.01f);
        await Assert.That(centred.X).IsEqualTo(20f + (260f - left.Width) / 2).Within(0.5f);
        await Assert.That(right.X).IsEqualTo(20f + (260f - left.Width)).Within(0.5f);
        // The right-aligned line ends at the right content edge, and the run rides with the line.
        await Assert.That(right.X + right.Width).IsEqualTo(280f).Within(0.5f);
        await Assert.That(centred.Runs[0].X).IsEqualTo(centred.X).Within(0.01f);
    }

    [Test]
    public async Task All_caps_upper_cases_the_run_text()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new Run { Text = "Hello", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11, AllCaps = true } }],
            Properties = new()
        };

        var line = (PlacedLine) Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];
        await Assert.That(line.Runs[0].Text).IsEqualTo("HELLO");
    }

    [Test]
    public async Task A_soft_line_break_splits_the_paragraph_into_lines()
    {
        // The parser emits a soft line break as a run of "\n"; it splits the paragraph into two lines.
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new Run { Text = "first", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } },
                new Run { Text = "\n", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } },
                new Run { Text = "second", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }
            ],
            Properties = new()
        };

        var lines = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[0].Runs[0].Text).IsEqualTo("first");
        await Assert.That(lines[1].Runs[0].Text).IsEqualTo("second");
        await Assert.That(lines[1].Y > lines[0].Y).IsTrue();
    }

    [Test]
    public async Task Justify_fills_non_last_lines_to_the_width_and_leaves_the_last_line_natural()
    {
        // 300pt wide, 20pt margins → 260pt available, right content edge at 280pt. A justified paragraph
        // long enough to wrap fills its non-last lines to that edge.
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 40)), new() { Alignment = TextAlignment.Justify });
        var lines = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().ToList();

        await Assert.That(lines.Count > 1).IsTrue();
        var firstLineRight = lines[0].Runs[^1].X + lines[0].Runs[^1].Width;
        var lastLineRight = lines[^1].Runs[^1].X + lines[^1].Runs[^1].Width;

        // The first line justifies to the right edge; the last line keeps its natural (shorter) width.
        await Assert.That(firstLineRight).IsEqualTo(280f).Within(2f);
        await Assert.That(firstLineRight > lastLineRight).IsTrue();
    }

    [Test]
    public async Task A_behind_text_header_image_paints_first_at_its_page_anchored_position()
    {
        var header = new HeaderFooterContent
        {
            Elements =
            [
                new FloatingImageElement
                {
                    ImageData = [1, 2, 3], WidthPoints = 600, HeightPoints = 800,
                    HorizontalPositionPoints = 0, VerticalPositionPoints = 0,
                    HorizontalAnchor = HorizontalAnchor.Page, VerticalAnchor = VerticalAnchor.Page,
                    BehindText = true
                }
            ]
        };

        var items = Fragmenter.Layout([P("body")], Page(200), header).Pages[0].Items;

        // The full-page header image paints first (behind the body), at the page-anchored origin.
        await Assert.That(items[0] is PlacedImage).IsTrue();
        var image = (PlacedImage) items[0];
        await Assert.That(image.X).IsEqualTo(0f).Within(0.5f);
        await Assert.That(image.Y).IsEqualTo(0f).Within(0.5f);
        await Assert.That(image.Width).IsEqualTo(600f).Within(0.5f);
        await Assert.That(items.OfType<PlacedLine>().Any()).IsTrue();
    }
}
