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

    static Run TextRun(string text) => new() { Text = text, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } };

    static Run TabRun() => new() { Text = "", IsTab = true, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } };

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
    public async Task A_shaded_paragraph_emits_a_full_column_band_behind_its_line()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new Run { Text = "TITLE", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
            Properties = new() { BackgroundColorHex = "E6E0F0", Alignment = TextAlignment.Center }
        };

        var items = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.ToList();
        var shading = items.OfType<PlacedShading>().Single();
        var line = items.OfType<PlacedLine>().Single();

        // The band spans the full 260pt column (300pt wide, 20pt margins), not the centred text's width,
        // and is emitted before the line so a painter draws it behind the glyphs.
        await Assert.That(shading.ColorHex).IsEqualTo("E6E0F0");
        await Assert.That(shading.X).IsEqualTo(20f).Within(0.5f);
        await Assert.That(shading.Width).IsEqualTo(260f).Within(0.5f);
        await Assert.That(shading.Height).IsEqualTo(line.Height).Within(0.01f);
        await Assert.That(items.IndexOf(shading) < items.IndexOf(line)).IsTrue();
    }

    [Test]
    public async Task A_bottom_bordered_paragraph_emits_a_border_box_below_its_text()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [new Run { Text = "Heading", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }],
            Properties = new()
            {
                Borders = new CellBorders { Bottom = new BorderEdge { IsVisible = true, ColorHex = "A6A6A6", WidthPoints = 0.8 } },
                BorderBottomSpacePoints = 4
            }
        };

        var items = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.ToList();
        var line = items.OfType<PlacedLine>().Single();
        var border = items.OfType<PlacedBorder>().Single();

        // Only the bottom edge shows; the box spans the full 260pt column and its bottom sits the 4pt border
        // space below the line, emitted after the line so a painter strokes it over the text.
        await Assert.That(border.Borders.Bottom.IsVisible).IsTrue();
        await Assert.That(border.Borders.Top.IsVisible).IsFalse();
        await Assert.That(border.X).IsEqualTo(20f).Within(0.5f);
        await Assert.That(border.Width).IsEqualTo(260f).Within(0.5f);
        await Assert.That(border.Y + border.Height).IsEqualTo(line.Y + line.Height + 4f).Within(0.5f);
        await Assert.That(items.IndexOf(border) > items.IndexOf(line)).IsTrue();
    }

    [Test]
    public async Task Header_text_repeats_in_the_header_band_above_the_body_on_every_page()
    {
        var header = new HeaderFooterContent { Elements = [P("My Header", new() { Alignment = TextAlignment.Center })] };
        var page = Page(400) with { HeaderDistance = 10 };
        var document = Fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, header);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        foreach (var laidOutPage in document.Pages)
        {
            var headerLine = laidOutPage.Items.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "My Header"));
            var bodyTop = laidOutPage.Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text!.StartsWith("body"))).Min(_ => _.Y);
            // The header sits at the header distance (10pt), above the body's top margin (20pt).
            await Assert.That(headerLine.Y).IsEqualTo(10f).Within(2f);
            await Assert.That(headerLine.Y < bodyTop).IsTrue();
        }
    }

    [Test]
    public async Task Footer_text_renders_near_the_page_bottom_with_the_page_number_resolved_per_page()
    {
        ParagraphElement PageFooter() => new()
        {
            Runs =
            [
                new Run { Text = "Page ", Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } },
                new Run { Text = "0", PageField = PageFieldKind.Page, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } }
            ],
            Properties = new() { Alignment = TextAlignment.Right }
        };
        var footer = new HeaderFooterContent { Elements = [PageFooter()] };
        var page = Page(400) with { FooterDistance = 20 };
        var document = Fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, footer: footer);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        for (var pageIndex = 0; pageIndex < 2; pageIndex++)
        {
            // The footer sits near the bottom (its bottom is the footer distance above the 400pt page edge).
            var footerRuns = document.Pages[pageIndex].Items.OfType<PlacedLine>()
                .Where(_ => _.Y > 340).SelectMany(_ => _.Runs).ToList();
            await Assert.That(footerRuns.Any(_ => _.Text == $"{pageIndex + 1}")).IsTrue();
        }
    }

    [Test]
    public async Task Even_pages_take_the_even_page_header_when_the_document_opts_in()
    {
        var header = new HeaderFooterContent { Elements = [P("Odd Header")] };
        var evenHeader = new HeaderFooterContent { Elements = [P("Even Header")] };
        var document = Fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], Page(400), header: header, evenPageHeader: evenHeader);

        bool HasHeader(int pageIndex, string text) =>
            document.Pages[pageIndex].Items.OfType<PlacedLine>().SelectMany(_ => _.Runs).Any(_ => _.Text == text);
        await Assert.That(HasHeader(0, "Odd Header")).IsTrue();
        await Assert.That(HasHeader(0, "Even Header")).IsFalse();
        await Assert.That(HasHeader(1, "Even Header")).IsTrue();
        await Assert.That(HasHeader(1, "Odd Header")).IsFalse();
    }

    [Test]
    public async Task A_num_pages_field_resolves_to_the_total_page_count()
    {
        Run Field(PageFieldKind kind) => new() { Text = "0", PageField = kind, Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 } };
        var footer = new HeaderFooterContent
        {
            Elements = [new ParagraphElement { Runs = [TextRun("Page "), Field(PageFieldKind.Page), TextRun(" of "), Field(PageFieldKind.NumberOfPages)] }]
        };
        var page = Page(400) with { FooterDistance = 20 };
        var document = Fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, footer: footer);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // Page 1's footer reads "Page 1 of 2": the PAGE field is this page (1), NUMPAGES the total (2).
        var footerTexts = document.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Y > 340).SelectMany(_ => _.Runs).Select(_ => _.Text).ToList();
        await Assert.That(footerTexts.Contains("1")).IsTrue();
        await Assert.That(footerTexts.Contains("2")).IsTrue();
    }

    [Test]
    public async Task A_title_page_takes_its_first_page_header_background_image()
    {
        FloatingImageElement Image(string tag) => new()
        {
            ImageData = System.Text.Encoding.ASCII.GetBytes(tag),
            WidthPoints = 100,
            HeightPoints = 100,
            BehindText = true
        };
        var page = Page(400) with { DifferentFirstPage = true };
        var document = Fragmenter.Layout(
            [P("body one"), new PageBreakElement(), P("body two")],
            page,
            header: new HeaderFooterContent { Elements = [Image("default")] },
            firstPageHeader: new HeaderFooterContent { Elements = [Image("first")] });

        // The behind-text header image follows the same variant as the header text: page 1's from the
        // first-page header, page 2's from the default.
        string ImageTag(int pageIndex) =>
            System.Text.Encoding.ASCII.GetString(document.Pages[pageIndex].Items.OfType<PlacedImage>().Single().Data);
        await Assert.That(ImageTag(0)).IsEqualTo("first");
        await Assert.That(ImageTag(1)).IsEqualTo("default");
    }

    [Test]
    public async Task A_footer_table_lays_out_in_the_footer_band()
    {
        var footerTable = new TableElement
        {
            Properties = new() { GridColumnWidths = [100, 100] },
            Rows = [new TableRow { Cells = [new TableCell { Content = [P("left")] }, new TableCell { Content = [P("right")] }] }]
        };
        var page = Page(400) with { FooterDistance = 20 };
        var document = Fragmenter.Layout([P("body")], page, footer: new HeaderFooterContent { Elements = [footerTable] });

        // The footer table lays out near the bottom of the 400pt page, carrying its two cells.
        var footerRow = document.Pages[0].Items.OfType<PlacedTableRow>().Single();
        await Assert.That(footerRow.Y).IsGreaterThan(340f);
        await Assert.That(footerRow.Cells.Count).IsEqualTo(2);
        await Assert.That(footerRow.Cells[1].Content.OfType<PlacedLine>().SelectMany(_ => _.Runs).Single().Text).IsEqualTo("right");
    }

    [Test]
    public async Task A_title_pages_footer_is_suppressed_when_it_has_no_first_page_footer()
    {
        var footer = new HeaderFooterContent { Elements = [P("Footer text")] };
        var page = Page(400) with { DifferentFirstPage = true, FooterDistance = 20 };
        // firstPageFooter is null, so page 1 (the title page) shows no footer while page 2 shows the default.
        var document = Fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, footer: footer);

        bool HasFooter(int pageIndex) => document.Pages[pageIndex].Items.OfType<PlacedLine>()
            .SelectMany(_ => _.Runs).Any(_ => _.Text == "Footer text");
        await Assert.That(HasFooter(0)).IsFalse();
        await Assert.That(HasFooter(1)).IsTrue();
    }

    [Test]
    public async Task A_tab_advances_the_following_text_to_the_next_default_tab_stop()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [TextRun("A"), TabRun(), TextRun("B")],
            Properties = new() { DefaultTabStopPoints = 36 }
        };
        var line = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
        var second = line.Runs.First(_ => _.Text == "B");
        // "A" starts at the line's left; the tab jumps to the first 36pt default stop, where "B" begins.
        await Assert.That(second.X - line.X).IsEqualTo(36f).Within(1f);
    }

    [Test]
    public async Task A_right_tab_stop_right_aligns_the_following_text_to_end_at_the_stop()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [TextRun("Chapter"), TabRun(), TextRun("12")],
            Properties = new() { TabStops = [new TabStop { PositionPoints = 200, Alignment = TabAlignment.Right }] }
        };
        var line = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
        var number = line.Runs.First(_ => _.Text == "12");
        // The number is right-aligned so its right edge sits at the 200pt stop (from the column's left edge).
        await Assert.That(number.X - line.X + number.Width).IsEqualTo(200f).Within(2f);
    }

    [Test]
    public async Task A_leadered_tab_stop_emits_a_leader_filler_across_the_gap()
    {
        var paragraph = new ParagraphElement
        {
            Runs = [TextRun("Chapter"), TabRun(), TextRun("1")],
            Properties = new() { TabStops = [new TabStop { PositionPoints = 200, Alignment = TabAlignment.Right, Leader = TabLeader.Dot }] }
        };
        var line = Fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
        var leader = line.Runs.Single(_ => _.Leader == TabLeader.Dot);
        var number = line.Runs.Single(_ => _.Text == "1");
        // The dot-leader filler carries no text and spans the gap from "Chapter" to the right-aligned "1".
        await Assert.That(string.IsNullOrEmpty(leader.Text)).IsTrue();
        await Assert.That(leader.Width > 0).IsTrue();
        await Assert.That(leader.X).IsGreaterThanOrEqualTo(line.X);
        await Assert.That(leader.X + leader.Width).IsLessThanOrEqualTo(number.X + 1f);
    }

    [Test]
    public async Task An_empty_paragraph_sizes_its_blank_line_by_its_mark_font()
    {
        ParagraphElement Empty(double markSize) => new()
        {
            Runs = [],
            Properties = new() { ParagraphMarkRunProperties = new() { FontFamily = "Aptos", FontSizePoints = markSize } }
        };

        float Height(double markSize) =>
            Fragmenter.Layout([Empty(markSize)], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single().Height;

        // A blank paragraph has no runs, so its spacer line follows the paragraph mark's font — a 24pt mark
        // makes a much taller line than an 8pt one, rather than both collapsing to a default size.
        await Assert.That(Height(24) > Height(8) + 10f).IsTrue();
    }

    [Test]
    public async Task Widow_control_moves_a_paragraph_that_would_orphan_its_first_line()
    {
        // Ten single-line fillers fill all but one of page 1's eleven lines; then a paragraph that wraps to
        // several lines. Without widow control its first line takes the last slot and the rest orphan onto
        // page 2; with it (Word's default) the whole paragraph moves so no single line is left behind.
        var fillers = Enumerable.Range(0, 10).Select(_ => P("filler")).ToArray();
        var text = string.Join(' ', Enumerable.Repeat("lorem", 40));
        ParagraphElement Tail(bool widowControl) => P(text, new ParagraphProperties { WidowControl = widowControl });

        int Page1TailLines(LaidOutDocument document) =>
            document.Pages[0].Items.OfType<PlacedLine>().Count(_ => _.Runs.Any(run => run.Text.Contains("lorem")));

        await Assert.That(Page1TailLines(Fragmenter.Layout([.. fillers, Tail(false)], Page(200)))).IsGreaterThan(0);
        await Assert.That(Page1TailLines(Fragmenter.Layout([.. fillers, Tail(true)], Page(200)))).IsEqualTo(0);
    }

    [Test]
    public async Task Keep_lines_moves_a_whole_paragraph_rather_than_splitting_it()
    {
        // Eight fillers leave three of page 1's lines free; a keep-lines paragraph that needs five lines
        // moves to page 2 intact rather than filling the three and continuing overleaf.
        var fillers = Enumerable.Range(0, 8).Select(_ => P("filler")).ToArray();
        var text = string.Join(' ', Enumerable.Repeat("lorem", 40));
        ParagraphElement Tail(bool keepLines) => P(text, new ParagraphProperties { KeepLines = keepLines });

        int Page1TailLines(LaidOutDocument document) =>
            document.Pages[0].Items.OfType<PlacedLine>().Count(_ => _.Runs.Any(run => run.Text.Contains("lorem")));

        await Assert.That(Page1TailLines(Fragmenter.Layout([.. fillers, Tail(false)], Page(200)))).IsGreaterThan(0);
        await Assert.That(Page1TailLines(Fragmenter.Layout([.. fillers, Tail(true)], Page(200)))).IsEqualTo(0);
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
    public async Task Interior_horizontal_borders_add_to_the_table_height()
    {
        // Word draws each collapsed interior edge on the row boundary and insets the row below it, so an
        // N-row bordered table is taller than the same table without inside borders by (N-1) x the border
        // width. Missing this, the engine fit an extra row per page in a dense bordered table
        // (header_row_repeat/01: 25 data rows per page vs Word's 24). The production render positions
        // content differently, so interior borders are opt-in (addInteriorBorders) on the engine path.
        TableElement ThreeRow(BorderEdge insideH) =>
            new()
            {
                Properties = new()
                {
                    GridColumnWidths = [120],
                    DefaultBorders = new CellBorders { Top = BorderEdge.None, Bottom = BorderEdge.None, Left = BorderEdge.None, Right = BorderEdge.None },
                    InsideHorizontalBorder = insideH
                },
                Rows =
                [
                    new TableRow { Cells = [new TableCell { Content = [P("row a")], Properties = new() }] },
                    new TableRow { Cells = [new TableCell { Content = [P("row b")], Properties = new() }] },
                    new TableRow { Cells = [new TableCell { Content = [P("row c")], Properties = new() }] }
                ]
            };

        float Total(TableElement table)
        {
            var rows = Fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().ToList();
            return rows[^1].Y + rows[^1].Height - rows[0].Y;
        }

        var withBorder = Total(ThreeRow(new BorderEdge { IsVisible = true, WidthPoints = 2.0 }));
        var without = Total(ThreeRow(BorderEdge.None));

        // 3 rows -> 2 interior edges -> 2 x 2.0pt taller.
        var delta = withBorder - without;
        await Assert.That(delta > 3.99f && delta < 4.01f).IsTrue();
    }

    static readonly string wrapping = string.Join(' ', Enumerable.Repeat("lorem", 60));

    [Test]
    public async Task First_line_indent_shifts_only_the_first_line_right()
    {
        var lines = Fragmenter.Layout([P(wrapping, new() { FirstLineIndentPoints = 24 })], Page(400))
            .Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count > 1).IsTrue();
        // First line 24pt right of the block; subsequent lines at the (zero) left indent.
        var shift = lines[0].X - lines[1].X;
        await Assert.That(shift > 23.5f && shift < 24.5f).IsTrue();
    }

    [Test]
    public async Task Hanging_indent_outdents_only_the_first_line_left()
    {
        var lines = Fragmenter.Layout([P(wrapping, new() { LeftIndentPoints = 40, HangingIndentPoints = 30 })], Page(400))
            .Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count > 1).IsTrue();
        // First line outdented 30pt LEFT of the subsequent lines, which sit at the 40pt left indent.
        var outdent = lines[1].X - lines[0].X;
        await Assert.That(outdent > 29.5f && outdent < 30.5f).IsTrue();
    }

    [Test]
    public async Task A_list_paragraphs_hanging_indent_does_not_shift_its_text()
    {
        // A list's hanging indent positions the marker, not the text: every text line stays at the left
        // indent. The general first-line shift must exempt lists or it double-outdents the wrapped text.
        var para = P(wrapping, new() { LeftIndentPoints = 36, HangingIndentPoints = 18, Numbering = new NumberingInfo { Text = "1." } });
        var lines = Fragmenter.Layout([para], Page(400)).Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count > 1).IsTrue();
        await Assert.That(Math.Abs(lines[0].X - lines[1].X) < 0.5f).IsTrue();
    }

    [Test]
    public async Task A_nested_table_lays_out_inside_its_cell_below_the_cells_paragraph()
    {
        var nested = new TableElement
        {
            Properties = new() { GridColumnWidths = [40, 40] },
            Rows = [new TableRow { Cells = [new TableCell { Content = [P("a")] }, new TableCell { Content = [P("b")] }] }]
        };
        var outer = new TableElement
        {
            Properties = new() { GridColumnWidths = [200] },
            Rows = [new TableRow { Cells = [new TableCell { Content = [P("before"), nested] }] }]
        };

        var cell = Fragmenter.Layout([outer], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];
        var nestedRow = cell.Content.OfType<PlacedTableRow>().Single();
        var beforeLine = cell.Content.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "before"));

        // The nested table's two cells carry their text, and the whole table sits below the cell's paragraph.
        await Assert.That(nestedRow.Cells.Count).IsEqualTo(2);
        await Assert.That(nestedRow.Cells[0].Content.OfType<PlacedLine>().SelectMany(_ => _.Runs).Single().Text).IsEqualTo("a");
        await Assert.That(nestedRow.Cells[1].Content.OfType<PlacedLine>().SelectMany(_ => _.Runs).Single().Text).IsEqualTo("b");
        await Assert.That(nestedRow.Y).IsGreaterThan(beforeLine.Y);
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

    [Test]
    public async Task A_behind_text_body_float_image_paints_behind_the_body_at_a_margin_relative_offset()
    {
        var image = new FloatingImageElement
        {
            ImageData = System.Text.Encoding.ASCII.GetBytes("PNG"),
            WidthPoints = 100, HeightPoints = 60,
            HorizontalPositionPoints = 10, VerticalPositionPoints = 15,
            HorizontalAnchor = HorizontalAnchor.Margin, VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var items = Fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.ToList();

        // The float paints first (behind the body line) at content-left + offset over content-top + offset.
        var placed = (PlacedImage) items[0];
        await Assert.That(placed.X).IsEqualTo(30f).Within(0.5f);
        await Assert.That(placed.Y).IsEqualTo(35f).Within(0.5f);
        await Assert.That(placed.Width).IsEqualTo(100f).Within(0.5f);
        await Assert.That(items.FindIndex(_ => _ is PlacedImage) < items.FindIndex(_ => _ is PlacedLine)).IsTrue();
    }

    [Test]
    public async Task An_svg_body_float_image_paints_its_raster_fallback()
    {
        var image = new FloatingImageElement
        {
            ImageData = System.Text.Encoding.ASCII.GetBytes("SVG"),
            ContentType = "image/svg+xml",
            RasterFallbackData = System.Text.Encoding.ASCII.GetBytes("PNG"),
            WidthPoints = 100, HeightPoints = 60,
            HorizontalAnchor = HorizontalAnchor.Margin, VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var placed = Fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.OfType<PlacedImage>().Single();

        // PdfSharp cannot rasterize SVG, so the float carries the raster equivalent, not the SVG bytes.
        await Assert.That(System.Text.Encoding.ASCII.GetString(placed.Data)).IsEqualTo("PNG");
    }

    [Test]
    public async Task An_image_filled_body_float_shape_paints_as_an_image()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 200, HeightPoints = 150,
            ImageData = System.Text.Encoding.ASCII.GetBytes("JPG"),
            ImageContentType = "image/jpeg",
            HorizontalAnchor = HorizontalAnchor.Margin, VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var items = Fragmenter.Layout([shape, P("body")], Page(200)).Pages[0].Items;

        // A full-bleed image-fill shape becomes a plain image — the shape painter skips image fills.
        var placed = items.OfType<PlacedImage>().Single();
        await Assert.That(System.Text.Encoding.ASCII.GetString(placed.Data)).IsEqualTo("JPG");
        await Assert.That(items.OfType<PlacedShape>().Any()).IsFalse();
    }

    [Test]
    public async Task A_gradient_body_float_shape_is_placed_carrying_its_gradient()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 100, HeightPoints = 40,
            Gradient = new GradientFill { StartColorHex = "FF0000", EndColorHex = "0000FF", DirectionDegrees = 0 },
            HorizontalAnchor = HorizontalAnchor.Margin, VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var items = Fragmenter.Layout([shape, P("body")], Page(200)).Pages[0].Items;

        // A gradient-filled shape is placed as a shape (the painter fills it with a linear gradient), not
        // dropped — its gradient stops survive to the painter.
        var placed = items.OfType<PlacedShape>().Single();
        await Assert.That(placed.Shape.Gradient).IsNotNull();
        await Assert.That(placed.Shape.Gradient!.StartColorHex).IsEqualTo("FF0000");
        await Assert.That(placed.Shape.Gradient!.EndColorHex).IsEqualTo("0000FF");
    }

    [Test]
    public async Task An_in_front_body_float_image_paints_over_the_body()
    {
        var image = new FloatingImageElement
        {
            ImageData = System.Text.Encoding.ASCII.GetBytes("PNG"),
            WidthPoints = 100, HeightPoints = 60,
            HorizontalAnchor = HorizontalAnchor.Margin, VerticalAnchor = VerticalAnchor.Margin,
            BehindText = false
        };

        var items = Fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.ToList();

        // A not-behind float paints last, over the body line.
        await Assert.That(items.FindIndex(_ => _ is PlacedImage) > items.FindIndex(_ => _ is PlacedLine)).IsTrue();
    }

    [Test]
    public async Task A_body_float_image_carries_its_rotation_flip_and_clip_to_the_painter()
    {
        var image = new FloatingImageElement
        {
            ImageData = System.Text.Encoding.ASCII.GetBytes("PNG"),
            WidthPoints = 100, HeightPoints = 60,
            RotationDegrees = 90, FlipHorizontal = true, ClipToEllipse = true,
            HorizontalAnchor = HorizontalAnchor.Margin, VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var placed = Fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.OfType<PlacedImage>().Single();

        // The DrawingML transforms flow through to the placed image (the painter applies them).
        await Assert.That(placed.RotationDegrees).IsEqualTo(90d).Within(0.01);
        await Assert.That(placed.FlipHorizontal).IsTrue();
        await Assert.That(placed.ClipToEllipse).IsTrue();
    }

    [Test]
    public async Task An_inline_image_carries_its_rotation_to_the_painter()
    {
        var run = new Run
        {
            Text = "",
            InlineImageData = System.Text.Encoding.ASCII.GetBytes("PNG"),
            InlineImageWidthPoints = 40,
            InlineImageHeightPoints = 40,
            InlineImageRotationDegrees = 45,
            Properties = new() { FontFamily = "Aptos", FontSizePoints = 11 }
        };
        var paragraph = new ParagraphElement { Runs = [run], Properties = new() };

        var image = Fragmenter.Layout([paragraph], Page(200)).Pages[0].Items.OfType<PlacedLine>().SelectMany(_ => _.Images).Single();

        // An inline image's transform reaches the painter the same way a floating one's does.
        await Assert.That(image.RotationDegrees).IsEqualTo(45d).Within(0.01);
    }

    [Test]
    public async Task Contextual_spacing_collapses_the_gap_between_same_style_paragraphs()
    {
        var properties = new ParagraphProperties { StyleId = "MemoHead", ContextualSpacing = true, SpacingAfterPoints = 12 };
        var document = Fragmenter.Layout([P("To", properties), P("From", properties), P("CC", properties)], Page(400));
        var lines = document.Pages[0].Items.OfType<PlacedLine>().ToList();

        // Three same-style contextual paragraphs stack with no inter-paragraph spacing — each line's top is
        // exactly the previous line's bottom, not offset by the 12pt after-spacing.
        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[1].Y).IsEqualTo(lines[0].Y + lines[0].Height).Within(0.01f);
        await Assert.That(lines[2].Y).IsEqualTo(lines[1].Y + lines[1].Height).Within(0.01f);
    }

    [Test]
    public async Task Contextual_spacing_does_not_collapse_across_different_styles()
    {
        var head = new ParagraphProperties { StyleId = "A", ContextualSpacing = true, SpacingAfterPoints = 12 };
        var body = new ParagraphProperties { StyleId = "B", ContextualSpacing = true, SpacingAfterPoints = 12 };
        var document = Fragmenter.Layout([P("head", head), P("body", body)], Page(400));
        var lines = document.Pages[0].Items.OfType<PlacedLine>().ToList();

        // A different StyleId breaks the collapse, so the 12pt after-spacing sits between the two lines.
        await Assert.That(lines[1].Y - (lines[0].Y + lines[0].Height)).IsEqualTo(12f).Within(0.01f);
    }

    [Test]
    public async Task A_trailing_empty_paragraph_that_overflows_does_not_add_a_page()
    {
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();
        var document = Fragmenter.Layout([.. fillers, P("")], Page(200));

        // The 11 fillers fill page 1; the trailing empty paragraph would overflow to page 2, but a page with
        // only a blank spacer line is a natural overflow blank Word drops — so the document stays one page.
        await Assert.That(document.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_trailing_paragraph_with_text_that_overflows_still_adds_a_page()
    {
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();
        var document = Fragmenter.Layout([.. fillers, P("overflow")], Page(200));

        // A twelfth paragraph carrying real text overflows onto a second page — only a blank trailing page
        // is absorbed, so this stays two pages.
        await Assert.That(document.Pages.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_next_page_section_break_starts_a_new_page()
    {
        var document = Fragmenter.Layout(
            [P("first"), new SectionBreakElement { BreakType = SectionBreakType.NextPage }, P("second")],
            Page(400));

        // A NextPage section break behaves like a page break: the following content starts a fresh page.
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[1].Items.OfType<PlacedLine>().SelectMany(_ => _.Runs).Any(_ => _.Text == "second")).IsTrue();
    }

    [Test]
    public async Task A_continuous_section_break_keeps_content_on_the_same_page()
    {
        var document = Fragmenter.Layout(
            [P("first"), new SectionBreakElement { BreakType = SectionBreakType.Continuous }, P("second")],
            Page(400));

        // A continuous section break at the same geometry takes no flow space — both paragraphs stay on page 1.
        await Assert.That(document.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_next_page_section_break_switches_page_geometry()
    {
        var second = new PageSettings { WidthPoints = 400, HeightPoints = 300, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 };
        var document = Fragmenter.Layout(
            [P("first"), new SectionBreakElement { BreakType = SectionBreakType.NextPage, NewSectionSettings = second }, P("second")],
            Page(200));

        // Page 1 keeps the document's original 300pt-wide geometry; page 2 adopts the new section's 400x300.
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[0].Settings.WidthPoints).IsEqualTo(300d).Within(0.01);
        await Assert.That(document.Pages[1].Settings.WidthPoints).IsEqualTo(400d).Within(0.01);
        await Assert.That(document.Pages[1].Settings.HeightPoints).IsEqualTo(300d).Within(0.01);
    }

    [Test]
    public async Task An_odd_page_section_break_inserts_a_blank_filler_page_for_parity()
    {
        // "first" fills page 1 (odd). An OddPage break wants the new section on an odd page, but the next
        // page would be 2 (even), so a blank page 2 is inserted and "second" lands on page 3.
        var document = Fragmenter.Layout(
            [P("first"), new SectionBreakElement { BreakType = SectionBreakType.OddPage }, P("second")],
            Page(200));

        await Assert.That(document.Pages.Count).IsEqualTo(3);
        await Assert.That(document.Pages[1].Items.OfType<PlacedLine>().Any(_ => _.Runs.Any(run => !string.IsNullOrWhiteSpace(run.Text)))).IsFalse();
        await Assert.That(document.Pages[2].Items.OfType<PlacedLine>().SelectMany(_ => _.Runs).Any(_ => _.Text == "second")).IsTrue();
    }

    [Test]
    public async Task A_continuous_column_break_starts_both_columns_at_the_break_below_the_masthead()
    {
        // A full-width masthead, then a continuous break to two columns: the classic newsletter shape. The
        // two columns (x=20 and x=20+120+20=160) both begin at the break point under the masthead, not the
        // page top.
        var twoColumns = new PageSettings { WidthPoints = 300, HeightPoints = 120, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20, ColumnCount = 2, ColumnSpacing = 20 };
        var body = Enumerable.Range(0, 12).Select(_ => P("body")).ToArray();
        var document = Fragmenter.Layout(
            [P("MASTHEAD"), new SectionBreakElement { BreakType = SectionBreakType.Continuous, NewSectionSettings = twoColumns }, .. body],
            Page(120));

        var lines = document.Pages[0].Items.OfType<PlacedLine>().ToList();
        var masthead = lines.Single(_ => _.Runs.Any(run => run.Text == "MASTHEAD"));
        var breakY = masthead.Y + masthead.Height;
        var bodyLines = lines.Where(_ => _.Runs.Any(run => run.Text == "body")).ToList();
        var column0 = bodyLines.Where(_ => _.X < 100f).ToList();
        var column1 = bodyLines.Where(_ => _.X > 100f).ToList();

        await Assert.That(masthead.Y).IsEqualTo(20f).Within(0.5f);
        await Assert.That(column0.Count > 0).IsTrue();
        await Assert.That(column1.Count > 0).IsTrue();
        // Both columns top out at the break Y, and the second column sits at the two-column offset.
        await Assert.That(column0.Min(_ => _.Y)).IsEqualTo(breakY).Within(0.5f);
        await Assert.That(column1.Min(_ => _.Y)).IsEqualTo(breakY).Within(0.5f);
        await Assert.That(column1.Min(_ => _.X)).IsEqualTo(160f).Within(0.5f);
    }

    [Test]
    public async Task A_continuous_columns_overflow_resets_the_next_page_to_its_top()
    {
        // The two-column section is tall enough to overflow onto a second page; there, with no masthead, the
        // columns reset to the page top (content top = 20) rather than the first page's break Y.
        var twoColumns = new PageSettings { WidthPoints = 300, HeightPoints = 120, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20, ColumnCount = 2, ColumnSpacing = 20 };
        var body = Enumerable.Range(0, 40).Select(_ => P("body")).ToArray();
        var document = Fragmenter.Layout(
            [P("MASTHEAD"), new SectionBreakElement { BreakType = SectionBreakType.Continuous, NewSectionSettings = twoColumns }, .. body],
            Page(120));

        await Assert.That(document.Pages.Count > 1).IsTrue();
        var page2Lines = document.Pages[1].Items.OfType<PlacedLine>().ToList();
        await Assert.That(page2Lines.Min(_ => _.Y)).IsEqualTo(20f).Within(0.5f);
    }

    // A three-column geometry with columns at x=0, 156, 312 (468pt wide, no margins/spacing).
    static PageSettings ThreeColumnSheet(int columns) =>
        new() { WidthPoints = 468, HeightPoints = 600, MarginTop = 0, MarginBottom = 0, MarginLeft = 0, MarginRight = 0, ColumnCount = columns, ColumnSpacing = 0 };

    [Test]
    public async Task A_multi_column_section_terminated_by_a_break_balances_its_columns()
    {
        // Six short items in a three-column section, then a continuous break to one column. Word balances the
        // terminated section's columns to equal heights — two items each — rather than newspaper-filling
        // column 0 (which, on a 600pt-tall page, would hold all six).
        var items = Enumerable.Range(1, 6).Select(_ => P($"Item {_}")).ToArray();
        var document = Fragmenter.Layout(
            [.. items, new SectionBreakElement { BreakType = SectionBreakType.Continuous, NewSectionSettings = ThreeColumnSheet(1) }, P("footer")],
            ThreeColumnSheet(3));

        var itemLines = document.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text.StartsWith("Item"))).ToList();
        await Assert.That(itemLines.Count(_ => _.X < 100f)).IsEqualTo(2);
        await Assert.That(itemLines.Count(_ => _.X is >= 100f and < 250f)).IsEqualTo(2);
        await Assert.That(itemLines.Count(_ => _.X >= 250f)).IsEqualTo(2);
    }

    [Test]
    public async Task A_multi_column_final_section_stays_newspaper_flowed()
    {
        // The same six items as a three-column section that ends the document (no terminating break). Word
        // does not balance this — column 0 fills first — so all six short items land in the first column.
        var items = Enumerable.Range(1, 6).Select(_ => P($"Item {_}")).ToArray();
        var document = Fragmenter.Layout([.. items], ThreeColumnSheet(3));

        var itemLines = document.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text.StartsWith("Item"))).ToList();
        await Assert.That(itemLines.Count).IsEqualTo(6);
        await Assert.That(itemLines.All(_ => _.X < 100f)).IsTrue();
    }
}
