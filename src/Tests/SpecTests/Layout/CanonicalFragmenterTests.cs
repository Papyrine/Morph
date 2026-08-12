/// <summary>
/// Tests the block-flow slice of the <see cref="Fragmenter"/> (step 3 of
/// <c>docs/layout-engine.md</c>): single-column pagination with line-level page breaks and the
/// height-model spacing rules. A small page geometry forces the interesting boundaries.
/// </summary>
public class CanonicalFragmenterTests
{
    static readonly Fragmenter fragmenter = new(LayoutTestFonts.Measurer);

    static readonly string wrapping = string.Join(' ', Enumerable.Repeat("lorem", 60));

    // A 1x1 PNG: the float only has to decode, its drawn pixels are irrelevant to where text lands.
    static readonly byte[] pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // 300pt wide, 20pt margins → 260pt measure. At 200pt tall the content band is 160pt = 11 Aptos-11
    // lines (14.5pt each), so the twelfth line breaks the page.
    static PageSettings Page(double heightPoints) =>
        new()
        {
            WidthPoints = 300,
            HeightPoints = heightPoints,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20
        };

    // The same geometry with N equal columns at a 20pt gap. At 2 columns the 260pt measure splits into
    // 120pt columns; column 1's left edge sits at 20 + 120 + 20 = 160pt.
    static PageSettings ColumnPage(double heightPoints, int columns) =>
        new()
        {
            WidthPoints = 300,
            HeightPoints = heightPoints,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20,
            ColumnCount = columns,
            ColumnSpacing = 20
        };

    static ParagraphElement P(string text, ParagraphProperties? properties = null) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = text,
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = properties ?? new()
        };

    static Run TextRun(string text) => new()
    {
        Text = text,
        Properties = new()
        {
            FontFamily = "Aptos",
            FontSizePoints = 11
        }
    };

    static Run TabRun() => new()
    {
        Text = "",
        IsTab = true,
        Properties = new()
        {
            FontFamily = "Aptos",
            FontSizePoints = 11
        }
    };

    /// <summary>
    /// Under <c>w:lineRule="exact"</c> the declared height is an absolute reservation, so the whole line
    /// box has to be inside the bottom margin — a line whose baseline clears it but whose box does not is
    /// pushed to the next page. Word-probed (<c>_probe_lastline_flow</c>, 60 paragraphs of 13pt exact
    /// lines against a 648pt band): Word keeps 49 and rejects a 50th whose box ends 2pt past the margin
    /// but whose baseline sits 0.53pt inside it.
    /// </summary>
    [Test]
    public async Task An_exact_spaced_line_needs_its_whole_box_inside_the_margin()
    {
        var page = Page(200);
        var properties = new ParagraphProperties
        {
            LineSpacingRule = LineSpacingRule.Exactly,
            LineSpacingPoints = 13.5
        };
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 98)), properties);

        // Guard: the geometry only tests the rule while the twelfth line is the discriminating one — its
        // baseline inside the 180pt content bottom, its box past it. Sitting on either side of both
        // bounds would make the test pass for the wrong reason.
        var lines = LayoutTestFonts.Measurer.LayoutLineContents(paragraph, (float) page.ContentWidth);
        await Assert.That(lines.Count).IsGreaterThanOrEqualTo(13);
        var twelfthTop = 20 + 11 * lines[0].Height;
        await Assert.That(twelfthTop + lines[11].Ascent).IsLessThanOrEqualTo(180f);
        await Assert.That(twelfthTop + lines[11].Height).IsGreaterThan(180f);

        var document = fragmenter.Layout([paragraph], page);

        await Assert.That(document.Pages[0].Items.OfType<PlacedLine>().Count()).IsEqualTo(11);
    }

    /// <summary>
    /// Under auto spacing only the baseline has to clear the bottom margin: Word lets the last line's
    /// descent and trailing gap encroach it rather than pushing the line to the next page, drawing the
    /// overhang and clipping it at the text area. Word-probed twice — <c>_probe_lastline_auto_flow</c>
    /// keeps a 42nd line whose box ends 0.56pt past the margin, and <c>image_wrap_square</c>'s column
    /// keeps a line whose box ends 4.36pt past it (and whose ink band stops dead on the boundary).
    /// This is the half of the rule that must NOT tighten with the exact case: the full box costs
    /// <c>image_wrap_square</c> Word's page count, which is how two earlier readings were caught.
    /// </summary>
    [Test]
    public async Task An_auto_spaced_line_keeps_only_its_baseline_inside_the_margin()
    {
        // 197pt tall puts the content bottom at 177, between the eleventh line's baseline and its box.
        var page = Page(197);
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 98)));

        var lines = LayoutTestFonts.Measurer.LayoutLineContents(paragraph, (float) page.ContentWidth);
        await Assert.That(lines.Count).IsGreaterThanOrEqualTo(12);
        var eleventhTop = 20 + 10 * lines[0].Height;
        await Assert.That(eleventhTop + lines[10].Ascent).IsLessThanOrEqualTo(177f);
        await Assert.That(eleventhTop + lines[10].Height).IsGreaterThan(177f);

        var document = fragmenter.Layout([paragraph], page);

        await Assert.That(document.Pages[0].Items.OfType<PlacedLine>().Count()).IsEqualTo(11);
    }

    /// <summary>
    /// <c>atLeast</c> reserves the whole box too, and does so even where the declared value LOSES to the
    /// font's natural pitch — the box is then the font's own, identical to what single auto produces, and
    /// Word is still strict. So the leniency above belongs to the auto rule itself, not to how the box was
    /// derived. Word-probed three ways (<c>_probe_lastline_atleast_a/b/c</c>): strict at a declared 15.5pt
    /// (Word keeps 41 where the lenient reading takes 42), at 21pt (30 against 31), and at a declared 10pt
    /// beaten by Calibri's 13.4277pt natural pitch (44 against 45).
    /// The two cases need different page heights because their boxes differ: 16pt binds, where 10pt loses
    /// to the font and leaves the bare single-spaced pitch — which is NOT the auto test's box either, since
    /// the default <see cref="ParagraphProperties.LineSpacingMultiplier"/> is Word's 1.08.
    /// </summary>
    [Test]
    [Arguments(10, 199, 11)]
    [Arguments(16, 197, 9)]
    public async Task An_atLeast_spaced_line_needs_its_whole_box_inside_the_margin(double declaredPoints, double pageHeight, int expected)
    {
        var page = Page(pageHeight);
        var contentBottom = (float) pageHeight - 20;
        var properties = new ParagraphProperties
        {
            LineSpacingRule = LineSpacingRule.AtLeast,
            LineSpacingPoints = declaredPoints
        };
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 98)), properties);

        // Guard: the line after the last one kept has to straddle the content bottom — baseline inside, box
        // outside — or the test would pass without separating the two readings. The 10pt case additionally
        // has to LOSE to the font, which is the whole point of it.
        var lines = LayoutTestFonts.Measurer.LayoutLineContents(paragraph, (float) page.ContentWidth);
        await Assert.That(lines.Count).IsGreaterThanOrEqualTo(expected + 2);
        await Assert.That(lines[0].Height > declaredPoints).IsEqualTo(declaredPoints < 14);
        var straddlingTop = 20 + expected * lines[0].Height;
        await Assert.That(straddlingTop + lines[expected].Ascent).IsLessThanOrEqualTo(contentBottom);
        await Assert.That(straddlingTop + lines[expected].Height).IsGreaterThan(contentBottom);

        var document = fragmenter.Layout([paragraph], page);

        await Assert.That(document.Pages[0].Items.OfType<PlacedLine>().Count()).IsEqualTo(expected);
    }

    /// <summary>
    /// A row that does not fit the space left is routed through the split path, but one that then fits
    /// whole on the fresh region is not split at all — it lands as a normal row with its declared
    /// <c>w:trHeight</c> honoured. <c>BuildRowFragment</c> sizes a fragment from its content alone, so
    /// letting it place a fits-whole row silently drops the atLeast floor. Word-probed
    /// (<c>_probe_trail2_nested</c>/<c>_bordered</c>): a 100pt row of ~25pt content carried to a fresh
    /// page puts the paragraph after the table at 174.72pt — margin plus the full declared height —
    /// where the fragment-sized row put it at 99.36.
    /// </summary>
    [Test]
    public async Task A_carried_row_keeps_its_declared_height()
    {
        var page = Page(200);
        // Eleven fillers reach 159.5pt, leaving half a point — too little even for the row's CONTENT
        // (the break decision ignores the atLeast floor, see the test below), so the row advances whole
        // to page 2 rather than leaving a fragment behind.
        var fillers = Enumerable.Range(0, 11).Select(index => P($"Filler {index}")).ToList();
        var table = new TableElement
        {
            Properties = new(),
            Rows =
            [
                new()
                {
                    HeightPoints = 100,
                    Cells =
                    [
                        new()
                        {
                            Content = [P("Short")],
                            Properties = new()
                        }
                    ]
                }
            ]
        };
        var after = P("AFTER");

        var document = fragmenter.Layout([.. fillers, table, after], page);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        var afterLine = document.Pages[1].Items.OfType<PlacedLine>().Single(_ => ReferenceEquals(_.Paragraph, after));
        // Content top 20 + the declared 100pt row, not 20 + the ~14.5pt its one line measures.
        await Assert.That(afterLine.Y).IsEqualTo(120f);
    }

    /// <summary>
    /// The atLeast floor PARTICIPATES in the BREAK decision: a row whose content would fit the space
    /// left but whose floor does not moves whole to the next region, floor honoured there. Word-probed
    /// four ways (<c>_probe_floorfit_single</c>/<c>_last</c>/<c>_mid</c>/<c>_enddoc</c>, 2026-08-07):
    /// a 30pt-floored row of one 12pt line offered a 24pt remainder moves in every structure — single-row
    /// table, last row, mid row, end of document — and business-plans/13's landscape pages break exactly
    /// where the floored row's floor crosses the bottom margin. A content-only fit was briefly landed off
    /// an in-situ letters/04 reading; that keep is upstream height drift (Word's letter runs ~50pt more
    /// compact, so the floor simply fits), not a fit law.
    /// </summary>
    [Test]
    public async Task A_floored_row_whose_content_fits_but_floor_does_not_moves_whole()
    {
        var page = Page(200);
        // Ten fillers reach 145pt, leaving 15pt — enough for the row's one ~14.5pt line, nowhere near
        // its 100pt floor.
        var fillers = Enumerable.Range(0, 10).Select(index => P($"Filler {index}")).ToList();
        var table = new TableElement
        {
            Properties = new(),
            Rows =
            [
                new()
                {
                    HeightPoints = 100,
                    Cells =
                    [
                        new()
                        {
                            Content = [P("Short")],
                            Properties = new()
                        }
                    ]
                }
            ]
        };
        var after = P("AFTER");

        var document = fragmenter.Layout([.. fillers, table, after], page);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // The row moves whole to page 2's region top at its full floor; nothing of it stays behind.
        await Assert.That(document.Pages[0].Items.OfType<PlacedTableRow>().Any()).IsFalse();
        var row = document.Pages[1].Items.OfType<PlacedTableRow>().Single();
        await Assert.That(row.Y).IsEqualTo(20f);
        await Assert.That(row.Height).IsEqualTo(100f);
        var afterLine = document.Pages[1].Items.OfType<PlacedLine>().Single(_ => ReferenceEquals(_.Paragraph, after));
        await Assert.That(afterLine.Y).IsEqualTo(120f);
    }

    /// <summary>
    /// Repeated <c>w:tblHeader</c> rows do not cost the row beneath them its declared
    /// <c>w:trHeight</c>. The floor above applies to a row CARRIED WHOLE TO A FRESH REGION, and a row
    /// carried there under re-emitted headers is still that row — but the header loop advances the
    /// cursor and clears the region-top flag, so reading the flag after it dropped the floor and the
    /// row came out content-sized. Measured on business-plans/13, whose start-up-costs table repeats
    /// three headers on each of pages 14, 16 and 20: the first data row rendered 11pt against its
    /// declared 21.6pt, shifting the eight rows below it 23px up the page at 150 DPI.
    /// </summary>
    [Test]
    public async Task A_row_carried_under_repeated_headers_keeps_its_declared_height()
    {
        var page = Page(200);
        // Nine fillers reach 130.5pt, leaving 29.5pt: room for the header row but not the floored row
        // below it, whose floor is what the break decision weighs. The remainder left after the header
        // is under one line, so the split path advances the whole row rather than leaving a stub — and
        // re-emits the header above it on page 2.
        var fillers = Enumerable.Range(0, 9).Select(index => P($"Filler {index}")).ToList();
        var table = new TableElement
        {
            Properties = new(),
            Rows =
            [
                new()
                {
                    IsHeader = true,
                    Cells =
                    [
                        new()
                        {
                            Content = [P("HEADER")],
                            Properties = new()
                        }
                    ]
                },
                new()
                {
                    HeightPoints = 100,
                    Cells =
                    [
                        new()
                        {
                            Content = [P("Short")],
                            Properties = new()
                        }
                    ]
                }
            ]
        };
        var after = P("AFTER");

        var document = fragmenter.Layout([.. fillers, table, after], page);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        var rows = document.Pages[1].Items.OfType<PlacedTableRow>().ToList();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].IsRepeatedHeader).IsTrue();
        await Assert.That(rows[0].Y).IsEqualTo(20f);
        // The floor survives the header above it, rather than collapsing to the one line "Short" measures.
        await Assert.That(rows[1].Height).IsEqualTo(100f);
        await Assert.That(rows[1].Y).IsEqualTo(20f + rows[0].Height);
        var afterLine = document.Pages[1].Items.OfType<PlacedLine>().Single(_ => ReferenceEquals(_.Paragraph, after));
        await Assert.That(afterLine.Y).IsEqualTo(rows[1].Y + 100f);
    }

    /// <summary>
    /// A vertical-merge CONTINUATION row never breaks from its predecessor — a page break between them
    /// would tear the merged cell apart, so it stacks under the row above wherever that landed,
    /// overflowing the bottom margin if it must. Word-measured on resumes/06: a sidebar table's
    /// restartless-continue rows run to the paper edge and clip (the black bar band at 750–792pt on a
    /// 792pt page) rather than moving to a page they would comfortably fit; the engine breaking before
    /// the continuation was exactly what turned that document from 3 pages into 6.
    /// </summary>
    [Test]
    public async Task A_merge_continuation_row_stacks_rather_than_breaking()
    {
        var page = Page(200);
        // Seven fillers reach 101.5pt, leaving 58.5pt — the 87pt table is fit-routed row by row. Row 0
        // (43.5pt) fits the remainder; row 1 is tied to it by the merge and must stack, not move.
        var fillers = Enumerable.Range(0, 7).Select(index => P($"Filler {index}")).ToList();

        static TableCell ContentCell(string prefix) =>
            new()
            {
                Content = [.. Enumerable.Range(0, 3).Select(index => P($"{prefix} {index}"))],
                Properties = new()
            };

        var table = new TableElement
        {
            Properties = new(),
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [],
                            Properties = new()
                            {
                                VerticalMerge = VerticalMergeType.Restart
                            }
                        },
                        ContentCell("Top")
                    ]
                },
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [],
                            Properties = new()
                            {
                                VerticalMerge = VerticalMergeType.Continue
                            }
                        },
                        ContentCell("Tied")
                    ]
                }
            ]
        };
        var after = P("AFTER");

        var document = fragmenter.Layout([.. fillers, table, after], page);

        // Both rows on page 1, the continuation stacked directly under row 0, its box past the 180pt
        // content bottom; only AFTER breaks to page 2.
        var rows = document.Pages[0].Items.OfType<PlacedTableRow>().ToList();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[1].Y).IsEqualTo(rows[0].Y + rows[0].Height);
        await Assert.That(rows[1].Y + rows[1].Height).IsGreaterThan(180f);
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        var afterLine = document.Pages[1].Items.OfType<PlacedLine>().Single(_ => ReferenceEquals(_.Paragraph, after));
        await Assert.That(afterLine.Y).IsEqualTo(20f);
    }

    /// <summary>
    /// Widow and orphan control are settled in ORDER, the orphan check acting on what the widow carry
    /// left — Word does not treat them as alternatives. A three-line paragraph with room for exactly two
    /// is the case that separates the two readings: the carry drops it to one line on this page, and the
    /// orphan rule then moves the whole paragraph. Checking them as mutually exclusive branches stops
    /// after the carry and leaves behind exactly the orphan the rule exists to prevent.
    /// No corpus document exercises this — the scenario suite is unchanged either way — so the rule is
    /// pinned here. It was verified against Word through the equivalent path in a splittable table row
    /// (business-plans/15's "Long-term Liabilities" bullet, where Word breaks 0/3 and the alternative
    /// reading gives 1/2).
    /// </summary>
    [Test]
    public async Task A_three_line_paragraph_with_room_for_two_moves_whole()
    {
        var page = Page(200);
        // Nine single-line paragraphs leave 160 - 9 × 14.5 = 29.5pt, which holds two more lines and not
        // a third.
        var fillers = Enumerable.Range(0, 9).Select(index => P($"Filler {index}")).ToList();
        var tail = P(string.Join(' ', Enumerable.Repeat("lorem", 21)));

        // Guard: the geometry above only tests the rule while the tail really is three lines and really
        // does have room for two.
        var tailLines = LayoutTestFonts.Measurer.LayoutLines(tail, (float) page.ContentWidth);
        await Assert.That(tailLines.Count).IsEqualTo(3);

        var document = fragmenter.Layout([.. fillers, tail], page);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // Nothing of the tail stays behind: page 1 keeps only the nine fillers.
        await Assert.That(document.Pages[0].Items.OfType<PlacedLine>().Count()).IsEqualTo(9);
        await Assert.That(document.Pages[1].Items.OfType<PlacedLine>().Count(_ => ReferenceEquals(_.Paragraph, tail))).IsEqualTo(3);
    }

    /// <summary>A lone line left at a region top is carried a second line down to join it.</summary>
    [Test]
    public async Task A_widow_carries_a_second_line_down_to_join_it()
    {
        var page = Page(200);
        // Eight fillers leave 44pt — three of the tail's four lines fit, so the fourth would sit alone
        // overleaf. The carry takes a second line with it, leaving two on each page.
        var fillers = Enumerable.Range(0, 8).Select(index => P($"Filler {index}")).ToList();
        var tail = P(string.Join(' ', Enumerable.Repeat("lorem", 29)));

        var tailLines = LayoutTestFonts.Measurer.LayoutLines(tail, (float) page.ContentWidth);
        await Assert.That(tailLines.Count).IsEqualTo(4);

        var document = fragmenter.Layout([.. fillers, tail], page);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[0].Items.OfType<PlacedLine>().Count(_ => ReferenceEquals(_.Paragraph, tail))).IsEqualTo(2);
        await Assert.That(document.Pages[1].Items.OfType<PlacedLine>().Count(_ => ReferenceEquals(_.Paragraph, tail))).IsEqualTo(2);
    }

    // A one-row table whose single cell holds `lines` short paragraphs — the shape that forces a row
    // taller than the page.
    static TableElement OneRowTable(int lines, bool cannotSplit) =>
        new()
        {
            Properties = new(),
            Rows =
            [
                new()
                {
                    CannotSplit = cannotSplit,
                    Cells =
                    [
                        new()
                        {
                            Content = [.. Enumerable.Range(0, lines).Select(index => P($"Row line {index}"))],
                            Properties = new()
                        }
                    ]
                }
            ]
        };

    /// <summary>
    /// A row taller than a whole region splits at a line boundary and continues overleaf, rather than
    /// overflowing off the page as it used to. business-plans/15 wraps whole prose sections in one-row
    /// tables and lost a page to that overflow.
    /// </summary>
    [Test]
    public async Task A_row_taller_than_the_page_splits_across_pages()
    {
        // 30 lines at 14.5pt is 435pt against a 160pt content band, so the row cannot fit any page.
        var document = fragmenter.Layout([OneRowTable(30, false)], Page(200));

        await Assert.That(document.Pages.Count > 1).IsTrue();
        // Every line is placed exactly once across the fragments, and none runs past the content bottom.
        var placed = document.Pages.SelectMany(_ => _.Items).OfType<PlacedTableRow>()
            .SelectMany(_ => _.Cells).SelectMany(_ => _.Content).OfType<PlacedLine>().ToList();
        await Assert.That(placed.Count).IsEqualTo(30);
        foreach (var line in placed)
        {
            await Assert.That(line.Y + line.Height).IsLessThanOrEqualTo(181f);
        }
    }

    /// <summary>
    /// <c>w:cantSplit</c> forbids the split even when splitting is the only way to show the content: Word
    /// lets such a row overflow the content area and clip at the paper edge instead. Word-probed
    /// (<c>_probe_cantsplit_tall_on</c>): the flagged row ran to 791.5pt on a 792pt page with the
    /// following paragraph alone overleaf, where the unflagged control split 53 lines / 17.
    /// No corpus document sets the attribute, so this is the only guard on it.
    /// </summary>
    [Test]
    public async Task A_cantSplit_row_is_not_split_even_when_it_cannot_fit()
    {
        var document = fragmenter.Layout([OneRowTable(30, true)], Page(200));

        // One row, placed once, overflowing rather than continuing overleaf.
        var rows = document.Pages.SelectMany(_ => _.Items).OfType<PlacedTableRow>().ToList();
        await Assert.That(rows.Count).IsEqualTo(1);
        var placed = rows.SelectMany(_ => _.Cells).SelectMany(_ => _.Content).OfType<PlacedLine>().ToList();
        await Assert.That(placed.Count).IsEqualTo(30);
        await Assert.That(placed.Max(_ => _.Y + _.Height)).IsGreaterThan(181f);
    }

    [Test]
    public async Task Short_paragraphs_fit_on_one_page()
    {
        var document = fragmenter.Layout([P("One"), P("Two"), P("Three")], Page(200));
        await Assert.That(document.Pages.Count).IsEqualTo(1);
        await Assert.That(document.Pages[0].Items.Count).IsEqualTo(3);
    }

    [Test]
    public async Task A_tall_paragraph_splits_across_pages_at_line_boundaries()
    {
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 220)));
        var page = Page(200);
        var totalLines = LayoutTestFonts.Measurer.LayoutLines(paragraph, (float) page.ContentWidth).Count;

        var document = fragmenter.Layout([paragraph], page);

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
        var moved = P("moved", new()
        {
            SpacingBeforePoints = 50
        });

        var document = fragmenter.Layout([.. fillers, moved], Page(200));

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // Its first line sits at the content top — the 50pt before was dropped, not applied.
        await Assert.That(document.Pages[1].Items[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task An_empty_paragraphs_after_spacing_shifts_the_following_paragraph_down()
    {
        static ParagraphElement Empty(double after) =>
            new()
            {
                Runs =
                [
                    new()
                    {
                        Text = "",
                        Properties = new()
                        {
                            FontFamily = "Aptos",
                            FontSizePoints = 11
                        }
                    }
                ],
                Properties = new()
                {
                    SpacingAfterPoints = after
                }
            };

        static float BeeY(LaidOutDocument document) =>
            document.Pages[0].Items.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "B")).Y;

        var withAfter = BeeY(fragmenter.Layout([P("A"), Empty(10), P("B")], Page(400)));
        var withoutAfter = BeeY(fragmenter.Layout([P("A"), Empty(0), P("B")], Page(400)));

        // An empty spacer paragraph carries its after-spacing into the gap before B (max-collapse with B's
        // zero before-spacing), so B sits 10pt lower than when the spacer has no after-spacing. Word applies
        // an empty paragraph's after-spacing like any other's — it is not a bare mark line.
        await Assert.That(withAfter - withoutAfter).IsEqualTo(10f).Within(0.5f);
    }

    [Test]
    public async Task A_line_whose_baseline_clears_the_bottom_margin_stays_on_the_page()
    {
        var probe = fragmenter.Layout([P("probe")], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
        var lineHeight = probe.Height;
        var ascent = probe.Baseline - probe.Y;

        // A content band that ends between the third line's baseline and its bottom: the third line's
        // descent must spill past the margin. Word keeps it on the page; the fragmenter mirrors that.
        var page = Page(40 + 2 * lineHeight + ascent + 0.5);

        var document = fragmenter.Layout([P("one"), P("two"), P("three")], page);
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
        static float Width(double tracking) =>
            fragmenter.Layout(
                    [
                        new ParagraphElement
                        {
                            Runs =
                            [
                                new()
                                {
                                    Text = "ACCOUNTANT",
                                    Properties = new()
                                    {
                                        FontFamily = "Aptos",
                                        FontSizePoints = 11,
                                        CharacterSpacingPoints = tracking
                                    }
                                }
                            ]
                        }
                    ],
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
            Runs =
            [
                new()
                {
                    Text = "TITLE",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
            {
                BackgroundColorHex = "E6E0F0",
                Alignment = TextAlignment.Center
            }
        };

        var items = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.ToList();
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
            Runs =
            [
                new()
                {
                    Text = "Heading",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
            {
                Borders = new()
                {
                    Bottom = new()
                    {
                        IsVisible = true,
                        ColorHex = "A6A6A6",
                        WidthPoints = 0.8
                    }
                },
                BorderBottomSpacePoints = 4
            }
        };

        var items = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.ToList();
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
        var header = new HeaderFooterContent
        {
            Elements =
            [
                P("My Header", new()
                {
                    Alignment = TextAlignment.Center
                })
            ]
        };
        var page = Page(400) with
        {
            HeaderDistance = 10
        };
        var document = fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, header);

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
        static ParagraphElement PageFooter() => new()
        {
            Runs =
            [
                new()
                {
                    Text = "Page ",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                },
                new()
                {
                    Text = "0",
                    PageField = PageFieldKind.Page,
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
            {
                Alignment = TextAlignment.Right
            }
        };

        var footer = new HeaderFooterContent
        {
            Elements = [PageFooter()]
        };
        var page = Page(400) with
        {
            FooterDistance = 20
        };
        var document = fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, footer: footer);

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
        var header = new HeaderFooterContent
        {
            Elements = [P("Odd Header")]
        };
        var evenHeader = new HeaderFooterContent
        {
            Elements = [P("Even Header")]
        };
        var document = fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], Page(400), header, evenPageHeader: evenHeader);

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
        static Run Field(PageFieldKind kind) => new()
        {
            Text = "0",
            PageField = kind,
            Properties = new()
            {
                FontFamily = "Aptos",
                FontSizePoints = 11
            }
        };

        var footer = new HeaderFooterContent
        {
            Elements =
            [
                new ParagraphElement
                {
                    Runs = [TextRun("Page "), Field(PageFieldKind.Page), TextRun(" of "), Field(PageFieldKind.NumberOfPages)]
                }
            ]
        };
        var page = Page(400) with
        {
            FooterDistance = 20
        };
        var document = fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, footer: footer);

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        // Page 1's footer reads "Page 1 of 2": the PAGE field is this page (1), NUMPAGES the total (2).
        var footerTexts = document.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Y > 340).SelectMany(_ => _.Runs).Select(_ => _.Text).ToList();
        await Assert.That(footerTexts.Contains("1")).IsTrue();
        await Assert.That(footerTexts.Contains("2")).IsTrue();
    }

    [Test]
    public async Task A_title_page_takes_its_first_page_header_background_image()
    {
        static FloatingImageElement Image(string tag) => new()
        {
            ImageData = Encoding.ASCII.GetBytes(tag),
            WidthPoints = 100,
            HeightPoints = 100,
            BehindText = true
        };

        var page = Page(400) with
        {
            DifferentFirstPage = true
        };
        var document = fragmenter.Layout(
            [P("body one"), new PageBreakElement(), P("body two")],
            page,
            new()
            {
                Elements = [Image("default")]
            },
            firstPageHeader: new()
            {
                Elements = [Image("first")]
            });

        // The behind-text header image follows the same variant as the header text: page 1's from the
        // first-page header, page 2's from the default.
        string ImageTag(int pageIndex) =>
            Encoding.ASCII.GetString(document.Pages[pageIndex].Items.OfType<PlacedImage>().Single().Data!);

        await Assert.That(ImageTag(0)).IsEqualTo("first");
        await Assert.That(ImageTag(1)).IsEqualTo("default");
    }

    [Test]
    public async Task A_behind_text_header_shape_paints_as_a_placed_shape()
    {
        var banner = new FloatingShapeElement
        {
            WidthPoints = 400,
            HeightPoints = 120,
            FillColorHex = "262626",
            FillAlpha = 0.1,
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            BehindText = true
        };
        var document = fragmenter.Layout(
            [P("body")],
            Page(400),
            new()
            {
                Elements = [banner]
            });

        // A header's behind-text shape paints as a PlacedShape ahead of the body — the engine used to emit
        // only header images and dropped shape banners (cover-letters/10's charcoal band). The painter
        // honours the shape's fill alpha (the banner's 10% accent tint).
        var shape = document.Pages[0].Items.OfType<PlacedShape>().Single();
        await Assert.That(shape.Shape.FillColorHex).IsEqualTo("262626");
        await Assert.That(shape.Shape.FillAlpha).IsEqualTo(0.1);
    }

    [Test]
    public async Task A_floating_text_box_emits_its_box_chrome_and_content()
    {
        var textBox = new FloatingTextBoxElement
        {
            Content = [P("boxed")],
            WidthPoints = 200,
            HeightPoints = 80,
            BackgroundColorHex = "C0E3EC",
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page
        };
        var items = fragmenter.Layout([textBox, P("body")], Page(400)).Pages[0].Items;

        // The box chrome paints as a shape and the box content lays out as its own line inside the box.
        var box = items.OfType<PlacedShape>().Single();
        await Assert.That(box.Shape.FillColorHex).IsEqualTo("C0E3EC");
        await Assert.That(box.Width).IsEqualTo(200f);
        var boxedLine = items.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "boxed"));
        await Assert.That(boxedLine.Y >= 0).IsTrue();
    }

    [Test]
    public async Task An_unwarped_wordart_block_emits_its_box_and_centred_text()
    {
        var wordArt = new WordArtElement
        {
            Text = "LOGO",
            WidthPoints = 150,
            HeightPoints = 40,
            BoxLineColorHex = "000000",
            BoxLineWidthPoints = 1,
            Transform = WordArtTransform.None
        };
        var items = fragmenter.Layout([wordArt, P("body")], Page(400)).Pages[0].Items;

        // The unwarped WordArt paints its box frame and its centred text.
        await Assert.That(items.OfType<PlacedShape>().Single().Shape.LineColorHex).IsEqualTo("000000");
        await Assert.That(items.OfType<PlacedLine>().Any(_ => _.Runs.Any(run => run.Text == "LOGO"))).IsTrue();
    }

    [Test]
    public async Task A_wordart_block_sits_below_the_previous_paragraph_s_after_spacing()
    {
        // WordArt carries no spacing of its own, so the gap before it is the previous paragraph's
        // after-spacing — as for a table or any other block. Without this the box rides up by that gap and
        // every later block follows it (business/06's memo header and wordart-envelope's warps both did).
        var spaced = new ParagraphProperties
        {
            SpacingAfterPoints = 20
        };
        var wordArt = new WordArtElement
        {
            Text = "LOGO",
            WidthPoints = 150,
            HeightPoints = 40,
            Transform = WordArtTransform.None
        };

        var withGap = fragmenter.Layout([P("above", spaced), wordArt], Page(400)).Pages[0].Items;
        var withoutGap = fragmenter.Layout([P("above"), wordArt], Page(400)).Pages[0].Items;

        var gapTop = withGap.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "LOGO")).Y;
        var flushTop = withoutGap.OfType<PlacedLine>().Single(_ => _.Runs.Any(run => run.Text == "LOGO")).Y;
        await Assert.That(gapTop - flushTop).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task A_footer_table_lays_out_in_the_footer_band()
    {
        var footerTable = new TableElement
        {
            Properties = new()
            {
                GridColumnWidths = [100, 100]
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("left")]
                        },
                        new()
                        {
                            Content = [P("right")]
                        }
                    ]
                }
            ]
        };
        var page = Page(400) with
        {
            FooterDistance = 20
        };
        var document = fragmenter.Layout(
            [P("body")],
            page,
            footer: new()
            {
                Elements = [footerTable]
            });

        // The footer table lays out near the bottom of the 400pt page, carrying its two cells.
        var footerRow = document.Pages[0].Items.OfType<PlacedTableRow>().Single();
        await Assert.That(footerRow.Y).IsGreaterThan(340f);
        await Assert.That(footerRow.Cells.Count).IsEqualTo(2);
        await Assert.That(footerRow.Cells[1].Content.OfType<PlacedLine>().SelectMany(_ => _.Runs).Single().Text).IsEqualTo("right");
    }

    static float BodyTop(HeaderFooterContent? header, PageSettings? page = null) =>
        fragmenter.Layout([P("body")], page ?? Page(400), header).Pages[0].Items
            .OfType<PlacedLine>()
            .First(_ => string.Concat(_.Runs.Select(run => run.Text)) == "body").Y;

    [Test]
    public async Task A_header_taller_than_the_top_margin_pushes_the_body_down()
    {
        // Word treats a positive top margin as a minimum (ECMA-376 §17.6.11): a header whose content reaches
        // past it moves the body down. Page(400)'s 20pt top margin is well under the 36pt header distance plus
        // two header lines, so a headed page starts its body far below a bare one.
        var header = new HeaderFooterContent
        {
            Elements = [P("header line one"), P("header line two")]
        };
        await Assert.That(BodyTop(header) > BodyTop(null) + 30f).IsTrue();
    }

    [Test]
    public async Task A_title_page_with_no_first_page_header_reserves_no_header_space()
    {
        // A DifferentFirstPage document with no first-page header shows nothing on page 1, so its default
        // header must not push page 1's body down (the over-reservation that regressed business/04).
        var header = new HeaderFooterContent
        {
            Elements = [P("default one"), P("default two")]
        };
        var page = Page(400) with
        {
            DifferentFirstPage = true
        };
        await Assert.That(Math.Abs(BodyTop(header, page) - (float) page.MarginTop) < 0.5f).IsTrue();
    }

    [Test]
    public async Task A_title_pages_footer_is_suppressed_when_it_has_no_first_page_footer()
    {
        var footer = new HeaderFooterContent
        {
            Elements = [P("Footer text")]
        };
        var page = Page(400) with
        {
            DifferentFirstPage = true,
            FooterDistance = 20
        };
        // firstPageFooter is null, so page 1 (the title page) shows no footer while page 2 shows the default.
        var document = fragmenter.Layout([P("body one"), new PageBreakElement(), P("body two")], page, footer: footer);

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
            Properties = new()
            {
                DefaultTabStopPoints = 36
            }
        };
        var line = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
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
            Properties = new()
            {
                TabStops =
                [
                    new()
                    {
                        PositionPoints = 200,
                        Alignment = TabAlignment.Right
                    }
                ]
            }
        };
        var line = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
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
            Properties = new()
            {
                TabStops =
                [
                    new()
                    {
                        PositionPoints = 200,
                        Alignment = TabAlignment.Right,
                        Leader = TabLeader.Dot
                    }
                ]
            }
        };
        var line = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single();
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
        static ParagraphElement Empty(double markSize) => new()
        {
            Runs = [],
            Properties = new()
            {
                ParagraphMarkRunProperties = new()
                {
                    FontFamily = "Aptos",
                    FontSizePoints = markSize
                }
            }
        };

        static float Height(double markSize) =>
            fragmenter.Layout([Empty(markSize)], Page(400)).Pages[0].Items.OfType<PlacedLine>().Single().Height;

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

        ParagraphElement Tail(bool widowControl) => P(text, new()
        {
            WidowControl = widowControl
        });

        static int Page1TailLines(LaidOutDocument document) =>
            document.Pages[0].Items.OfType<PlacedLine>().Count(_ => _.Runs.Any(run => run.Text.Contains("lorem")));

        await Assert.That(Page1TailLines(fragmenter.Layout([.. fillers, Tail(false)], Page(200)))).IsGreaterThan(0);
        await Assert.That(Page1TailLines(fragmenter.Layout([.. fillers, Tail(true)], Page(200)))).IsEqualTo(0);
    }

    [Test]
    public async Task Keep_lines_moves_a_whole_paragraph_rather_than_splitting_it()
    {
        // Eight fillers leave three of page 1's lines free; a keep-lines paragraph that needs five lines
        // moves to page 2 intact rather than filling the three and continuing overleaf.
        var fillers = Enumerable.Range(0, 8).Select(_ => P("filler")).ToArray();
        var text = string.Join(' ', Enumerable.Repeat("lorem", 40));

        ParagraphElement Tail(bool keepLines) => P(
            text,
            new()
            {
                KeepLines = keepLines
            });

        static int Page1TailLines(LaidOutDocument document) =>
            document.Pages[0].Items.OfType<PlacedLine>().Count(_ => _.Runs.Any(run => run.Text.Contains("lorem")));

        await Assert.That(Page1TailLines(fragmenter.Layout([.. fillers, Tail(false)], Page(200)))).IsGreaterThan(0);
        await Assert.That(Page1TailLines(fragmenter.Layout([.. fillers, Tail(true)], Page(200)))).IsEqualTo(0);
    }

    [Test]
    public async Task Page_break_element_starts_a_new_page()
    {
        var document = fragmenter.Layout([P("before"), new PageBreakElement(), P("after")], Page(400));
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[1].Items[0].Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task Empty_document_is_one_empty_page()
    {
        var document = fragmenter.Layout([], Page(200));
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

        var document = fragmenter.Layout([paragraph], page);
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

        var document = fragmenter.Layout([P("before"), new ColumnBreakElement(), P("after")], page);

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
        var document = fragmenter.Layout([P(string.Join(' ', Enumerable.Repeat("lorem", 800)))], ColumnPage(200, 2));

        await Assert.That(document.Pages.Count > 1).IsTrue();
        // Page 2 resumes at column 0 (x=20), content top.
        var page2First = (PlacedLine) document.Pages[1].Items[0];
        await Assert.That(page2First.X).IsEqualTo(20f).Within(0.5f);
        await Assert.That(page2First.Y).IsEqualTo(20f).Within(0.01f);
    }

    [Test]
    public async Task A_uniform_line_is_one_run_carrying_the_whole_text()
    {
        var line = (PlacedLine) fragmenter.Layout([P("hello world")], Page(400)).Pages[0].Items[0];

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
                new()
                {
                    Text = "plain ",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                },
                new()
                {
                    Text = "bold",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11,
                        Bold = true
                    }
                }
            ],
            Properties = new()
        };

        var line = (PlacedLine) fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];

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
            Properties = new()
            {
                GridColumnWidths = [120, 120]
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("left cell")],
                            Properties = new()
                        },
                        new()
                        {
                            Content = [P("right cell")],
                            Properties = new()
                        }
                    ]
                }
            ]
        };

        var row = fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();

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
        static TableElement OneCell(double before) =>
            new()
            {
                Properties = new()
                {
                    GridColumnWidths = [200]
                },
                Rows =
                [
                    new()
                    {
                        Cells =
                        [
                            new()
                            {
                                Content =
                                [
                                    P(
                                        "cell text",
                                        new()
                                        {
                                            SpacingBeforePoints = before
                                        })
                                ],
                                Properties = new()
                            }
                        ]
                    }
                ]
            };

        var withoutBefore = fragmenter.Layout([OneCell(0)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();
        var withBefore = fragmenter.Layout([OneCell(20)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();

        var withoutY = withoutBefore.Cells[0].Content.OfType<PlacedLine>().First().Y;
        var withY = withBefore.Cells[0].Content.OfType<PlacedLine>().First().Y;
        await Assert.That(withY - withoutY).IsEqualTo(20f).Within(0.5f);
    }

    [Test]
    public async Task A_bottom_aligned_cell_shifts_its_content_below_a_top_aligned_one()
    {
        // A tall neighbour forces a tall row; the short cell's line then sits far lower when bottom-aligned.
        static TableElement TwoCell(CellVerticalAlignment align) =>
            new()
            {
                Properties = new()
                {
                    GridColumnWidths = [120, 120]
                },
                Rows =
                [
                    new()
                    {
                        Cells =
                        [
                            new()
                            {
                                Content =
                                [
                                    P(string.Join(' ', Enumerable.Repeat("lorem", 60)))
                                ],
                                Properties = new()
                            },
                            new()
                            {
                                Content = [P("short")],
                                Properties = new()
                                {
                                    VerticalAlignment = align
                                }
                            }
                        ]
                    }
                ]
            };

        var topAligned = fragmenter.Layout([TwoCell(CellVerticalAlignment.Top)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();
        var bottomAligned = fragmenter.Layout([TwoCell(CellVerticalAlignment.Bottom)], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single();

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
        static TableElement ThreeRow(BorderEdge insideH) =>
            new()
            {
                Properties = new()
                {
                    GridColumnWidths = [120],
                    DefaultBorders = new()
                    {
                        Top = BorderEdge.None,
                        Bottom = BorderEdge.None,
                        Left = BorderEdge.None,
                        Right = BorderEdge.None
                    },
                    InsideHorizontalBorder = insideH
                },
                Rows =
                [
                    new()
                    {
                        Cells =
                        [
                            new()
                            {
                                Content = [P("row a")],
                                Properties = new()
                            }
                        ]
                    },
                    new()
                    {
                        Cells =
                        [
                            new()
                            {
                                Content = [P("row b")],
                                Properties = new()
                            }
                        ]
                    },
                    new()
                    {
                        Cells =
                        [
                            new()
                            {
                                Content = [P("row c")],
                                Properties = new()
                            }
                        ]
                    }
                ]
            };

        static float Total(TableElement table)
        {
            var rows = fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().ToList();
            return rows[^1].Y + rows[^1].Height - rows[0].Y;
        }

        var withBorder = Total(
            ThreeRow(
                new()
                {
                    IsVisible = true,
                    WidthPoints = 2.0
                }));
        var without = Total(ThreeRow(BorderEdge.None));

        // 3 rows -> 2 interior edges -> 2 x 2.0pt taller.
        var delta = withBorder - without;
        await Assert.That(delta is > 3.99f and < 4.01f).IsTrue();
    }

    [Test]
    public async Task First_line_indent_shifts_only_the_first_line_right()
    {
        var lines = fragmenter.Layout([
                P(wrapping, new()
                {
                    FirstLineIndentPoints = 24
                })
            ], Page(400))
            .Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count > 1).IsTrue();
        // First line 24pt right of the block; subsequent lines at the (zero) left indent.
        var shift = lines[0].X - lines[1].X;
        await Assert.That(shift is > 23.5f and < 24.5f).IsTrue();
    }

    [Test]
    public async Task Hanging_indent_outdents_only_the_first_line_left()
    {
        var lines = fragmenter.Layout([
                P(wrapping, new()
                {
                    LeftIndentPoints = 40,
                    HangingIndentPoints = 30
                })
            ], Page(400))
            .Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count > 1).IsTrue();
        // First line outdented 30pt LEFT of the subsequent lines, which sit at the 40pt left indent.
        var outdent = lines[1].X - lines[0].X;
        await Assert.That(outdent is > 29.5f and < 30.5f).IsTrue();
    }

    [Test]
    public async Task A_list_paragraphs_hanging_indent_does_not_shift_its_text()
    {
        // A list's hanging indent positions the marker, not the text: every text line stays at the left
        // indent. The general first-line shift must exempt lists or it double-outdents the wrapped text.
        var para = P(
            wrapping,
            new()
            {
                LeftIndentPoints = 36,
                HangingIndentPoints = 18,
                Numbering = new()
                {
                    Text = "1."
                }
            });
        var lines = fragmenter.Layout([para], Page(400)).Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(lines.Count > 1).IsTrue();
        await Assert.That(Math.Abs(lines[0].X - lines[1].X) < 0.5f).IsTrue();
    }

    [Test]
    public async Task A_nested_table_lays_out_inside_its_cell_below_the_cells_paragraph()
    {
        var nested = new TableElement
        {
            Properties = new()
            {
                GridColumnWidths = [40, 40]
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("a")]
                        },
                        new()
                        {
                            Content = [P("b")]
                        }
                    ]
                }
            ]
        };
        var outer = new TableElement
        {
            Properties = new()
            {
                GridColumnWidths = [200]
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("before"), nested]
                        }
                    ]
                }
            ]
        };

        var cell = fragmenter.Layout([outer], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];
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
            Properties = new()
            {
                GridColumnWidths = [200]
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("recipient")],
                            Floats = [shape],
                            Properties = new()
                        }
                    ]
                }
            ]
        };

        var cell = fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];

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
            Properties = new()
            {
                GridColumnWidths = [200]
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("recipient")],
                            Floats = [shape],
                            Properties = new()
                        }
                    ]
                }
            ]
        };

        var cell = fragmenter.Layout([table], Page(400)).Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];
        await Assert.That(cell.Content.OfType<PlacedShape>().Any()).IsFalse();
    }

    [Test]
    public async Task A_list_paragraph_places_its_marker_in_the_hanging_indent()
    {
        var paragraph = new ParagraphElement
        {
            Runs =
            [
                new()
                {
                    Text = "list item text",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
            {
                LeftIndentPoints = 36,
                HangingIndentPoints = 18,
                Numbering = new()
                {
                    Text = "1."
                }
            }
        };

        var line = (PlacedLine) fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];

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
            Runs =
            [
                new()
                {
                    Text = "",
                    InlineImageData = [1, 2, 3],
                    InlineImageWidthPoints = 100,
                    InlineImageHeightPoints = 80,
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
        };

        var line = (PlacedLine) fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];

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
        var left = (PlacedLine) fragmenter.Layout([P("word")], Page(400)).Pages[0].Items[0];
        var centred = (PlacedLine) fragmenter.Layout([
            P("word", new()
            {
                Alignment = TextAlignment.Center
            })
        ], Page(400)).Pages[0].Items[0];
        var right = (PlacedLine) fragmenter.Layout([
            P("word", new()
            {
                Alignment = TextAlignment.Right
            })
        ], Page(400)).Pages[0].Items[0];

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
            Runs =
            [
                new()
                {
                    Text = "Hello",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11,
                        AllCaps = true
                    }
                }
            ],
            Properties = new()
        };

        var line = (PlacedLine) fragmenter.Layout([paragraph], Page(400)).Pages[0].Items[0];
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
                new()
                {
                    Text = "first",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                },
                new()
                {
                    Text = "\n",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                },
                new()
                {
                    Text = "second",
                    Properties = new()
                    {
                        FontFamily = "Aptos",
                        FontSizePoints = 11
                    }
                }
            ],
            Properties = new()
        };

        var lines = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().ToList();
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
        var paragraph = P(string.Join(' ', Enumerable.Repeat("lorem", 40)), new()
        {
            Alignment = TextAlignment.Justify
        });
        var lines = fragmenter.Layout([paragraph], Page(400)).Pages[0].Items.OfType<PlacedLine>().ToList();

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
                    ImageData = [1, 2, 3],
                    WidthPoints = 600,
                    HeightPoints = 800,
                    HorizontalPositionPoints = 0,
                    VerticalPositionPoints = 0,
                    HorizontalAnchor = HorizontalAnchor.Page,
                    VerticalAnchor = VerticalAnchor.Page,
                    BehindText = true
                }
            ]
        };

        var items = fragmenter.Layout([P("body")], Page(200), header).Pages[0].Items;

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
            ImageData = "PNG"u8.ToArray(),
            WidthPoints = 100,
            HeightPoints = 60,
            HorizontalPositionPoints = 10,
            VerticalPositionPoints = 15,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var items = fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.ToList();

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
            ImageData = "SVG"u8.ToArray(),
            ContentType = "image/svg+xml",
            RasterFallbackData = "PNG"u8.ToArray(),
            WidthPoints = 100,
            HeightPoints = 60,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var placed = fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.OfType<PlacedImage>().Single();

        // PdfSharp cannot rasterize SVG, so the float carries the raster equivalent, not the SVG bytes.
        await Assert.That(Encoding.ASCII.GetString(placed.Data!)).IsEqualTo("PNG");
    }

    [Test]
    public async Task An_image_filled_body_float_shape_paints_as_an_image()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 200,
            HeightPoints = 150,
            ImageData = "JPG"u8.ToArray(),
            ImageContentType = "image/jpeg",
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var items = fragmenter.Layout([shape, P("body")], Page(200)).Pages[0].Items;

        // A full-bleed image-fill shape becomes a plain image — the shape painter skips image fills.
        var placed = items.OfType<PlacedImage>().Single();
        await Assert.That(Encoding.ASCII.GetString(placed.Data!)).IsEqualTo("JPG");
        await Assert.That(items.OfType<PlacedShape>().Any()).IsFalse();
    }

    [Test]
    public async Task A_gradient_body_float_shape_is_placed_carrying_its_gradient()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 100,
            HeightPoints = 40,
            Gradient = new()
            {
                StartColorHex = "FF0000",
                EndColorHex = "0000FF",
                DirectionDegrees = 0
            },
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var items = fragmenter.Layout([shape, P("body")], Page(200)).Pages[0].Items;

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
            ImageData = "PNG"u8.ToArray(),
            WidthPoints = 100,
            HeightPoints = 60,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = false
        };

        var items = fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.ToList();

        // A not-behind float paints last, over the body line.
        await Assert.That(items.FindIndex(_ => _ is PlacedImage) > items.FindIndex(_ => _ is PlacedLine)).IsTrue();
    }

    [Test]
    public async Task A_body_float_image_carries_its_rotation_flip_and_clip_to_the_painter()
    {
        var image = new FloatingImageElement
        {
            ImageData = "PNG"u8.ToArray(),
            WidthPoints = 100,
            HeightPoints = 60,
            RotationDegrees = 90,
            FlipHorizontal = true,
            ClipToEllipse = true,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };

        var placed = fragmenter.Layout([image, P("body")], Page(200)).Pages[0].Items.OfType<PlacedImage>().Single();

        // The DrawingML transforms flow through to the placed image (the painter applies them).
        await Assert.That(placed.RotationDegrees).IsEqualTo(90d).Within(0.01);
        await Assert.That(placed.FlipHorizontal).IsTrue();
        await Assert.That(placed.ClipToEllipse).IsTrue();
    }

    [Test]
    public async Task An_absolute_body_float_resolves_to_the_page_its_following_content_lands_on()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 100,
            HeightPoints = 40,
            FillColorHex = "FF0000",
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Margin,
            BehindText = true
        };
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();

        // The shape's element is reached while the cursor is still on page 1 (after its eleven lines), but it
        // precedes the paragraph that overflows to page 2. A margin anchor makes its position absolute, so it
        // belongs to the page carrying the content it anchors — page 2 — not the emit-time cursor page. Before
        // the deferral this stacked a page-2 background onto page 1 (brochures/01).
        var document = fragmenter.Layout([.. fillers, shape, P("page two")], Page(200));

        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[0].Items.OfType<PlacedShape>().Any()).IsFalse();
        await Assert.That(document.Pages[1].Items.OfType<PlacedShape>().Any()).IsTrue();
    }

    [Test]
    public async Task A_text_anchored_floating_table_flows_inline_below_the_preceding_content()
    {
        var floatingTable = new TableElement
        {
            Properties = new()
            {
                GridColumnWidths = [100, 100],
                IsFloating = true,
                FloatingVerticalAnchor = FloatingTableVerticalAnchor.Text,
                FloatingHorizontalAnchor = FloatingTableHorizontalAnchor.Margin
            },
            Rows =
            [
                new()
                {
                    Cells =
                    [
                        new()
                        {
                            Content = [P("date")]
                        },
                        new()
                        {
                            Content = [P("value")]
                        }
                    ]
                }
            ]
        };

        var above = P("above", new()
        {
            SpacingAfterPoints = 30
        });
        var items = fragmenter.Layout([above, floatingTable, P("below")], Page(400)).Pages[0].Items.ToList();
        var aboveLine = items.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "above"));
        var row = items.OfType<PlacedTableRow>().Single();
        var belowLine = items.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "below"));

        // A text-anchored floating table takes flow space rather than overlaying: the preceding paragraph's
        // 30pt after-spacing pushes it down (agendas-minutes/11's placeholder gap), and the following
        // paragraph clears its bottom instead of overlapping it.
        await Assert.That(row.Y).IsEqualTo(aboveLine.Y + aboveLine.Height + 30f).Within(0.5f);
        await Assert.That(belowLine.Y).IsGreaterThanOrEqualTo(row.Y + row.Height - 0.5f);
    }

    [Test]
    public async Task A_percentage_positioned_float_resolves_against_its_anchor_reference()
    {
        var shape = new FloatingShapeElement
        {
            WidthPoints = 40,
            HeightPoints = 40,
            FillColorHex = "0000FF",
            HorizontalAnchor = HorizontalAnchor.Page,
            VerticalAnchor = VerticalAnchor.Page,
            HorizontalPositionPercent = 0.5,
            VerticalPositionPercent = 0.5,
            BehindText = true
        };

        // Page(200) is 300pt wide × 200pt tall. A 50% page-anchored offset (wp14:pctPosHOffset/pctPosVOffset)
        // lands the shape's top-left at the page centre (150, 100), not at the anchor origin — the percentage
        // resolves as a fraction of the page dimension.
        var placed = fragmenter.Layout([shape, P("body")], Page(200)).Pages[0].Items.OfType<PlacedShape>().Single();
        await Assert.That(placed.X).IsEqualTo(150f).Within(0.5f);
        await Assert.That(placed.Y).IsEqualTo(100f).Within(0.5f);
    }

    [Test]
    public async Task An_inline_image_carries_its_rotation_to_the_painter()
    {
        var run = new Run
        {
            Text = "",
            InlineImageData = "PNG"u8.ToArray(),
            InlineImageWidthPoints = 40,
            InlineImageHeightPoints = 40,
            InlineImageRotationDegrees = 45,
            Properties = new()
            {
                FontFamily = "Aptos",
                FontSizePoints = 11
            }
        };
        var paragraph = new ParagraphElement
        {
            Runs = [run],
            Properties = new()
        };

        var image = fragmenter.Layout([paragraph], Page(200)).Pages[0].Items.OfType<PlacedLine>().SelectMany(_ => _.Images).Single();

        // An inline image's transform reaches the painter the same way a floating one's does.
        await Assert.That(image.RotationDegrees).IsEqualTo(45d).Within(0.01);
    }

    [Test]
    public async Task An_image_only_paragraph_reserves_its_font_line_height_not_the_image_height()
    {
        var rule = new Run
        {
            Text = "",
            InlineImageData = "PNG"u8.ToArray(),
            InlineImageWidthPoints = 200,
            InlineImageHeightPoints = 0.5,
            Properties = new()
            {
                FontFamily = "Aptos",
                FontSizePoints = 11
            }
        };
        var ruleParagraph = new ParagraphElement
        {
            Runs = [rule],
            Properties = new()
        };
        var items = fragmenter.Layout([ruleParagraph, P("below")], Page(400)).Pages[0].Items.ToList();
        var belowLine = items.OfType<PlacedLine>().First(_ => _.Runs.Any(run => run.Text == "below"));

        // A 0.5pt-tall inline drawing in an otherwise-empty paragraph (a heading-rule template) must still
        // reserve the paragraph's own font line height (~14.5pt for Aptos-11), so the next paragraph clears
        // it — without that the line collapses to the image height and the rule drops onto the next line.
        await Assert.That(belowLine.Y).IsGreaterThan(12f);
    }

    [Test]
    public async Task Contextual_spacing_collapses_the_gap_between_same_style_paragraphs()
    {
        var properties = new ParagraphProperties
        {
            StyleId = "MemoHead",
            ContextualSpacing = true,
            SpacingAfterPoints = 12
        };
        var document = fragmenter.Layout([P("To", properties), P("From", properties), P("CC", properties)], Page(400));
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
        var head = new ParagraphProperties
        {
            StyleId = "A",
            ContextualSpacing = true,
            SpacingAfterPoints = 12
        };
        var body = new ParagraphProperties
        {
            StyleId = "B",
            ContextualSpacing = true,
            SpacingAfterPoints = 12
        };
        var document = fragmenter.Layout([P("head", head), P("body", body)], Page(400));
        var lines = document.Pages[0].Items.OfType<PlacedLine>().ToList();

        // A different StyleId breaks the collapse, so the 12pt after-spacing sits between the two lines.
        await Assert.That(lines[1].Y - (lines[0].Y + lines[0].Height)).IsEqualTo(12f).Within(0.01f);
    }

    [Test]
    public async Task A_trailing_empty_paragraph_that_overflows_does_not_add_a_page()
    {
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();
        var document = fragmenter.Layout([.. fillers, P("")], Page(200));

        // The 11 fillers fill page 1; the trailing empty paragraph would overflow to page 2, but a page with
        // only a blank spacer line is a natural overflow blank Word drops — so the document stays one page.
        await Assert.That(document.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_trailing_paragraph_with_text_that_overflows_still_adds_a_page()
    {
        var fillers = Enumerable.Range(0, 11).Select(_ => P("filler")).ToArray();
        var document = fragmenter.Layout([.. fillers, P("overflow")], Page(200));

        // A twelfth paragraph carrying real text overflows onto a second page — only a blank trailing page
        // is absorbed, so this stays two pages.
        await Assert.That(document.Pages.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_next_page_section_break_starts_a_new_page()
    {
        var document = fragmenter.Layout(
            [
                P("first"), new SectionBreakElement
                {
                    BreakType = SectionBreakType.NextPage
                },
                P("second")
            ],
            Page(400));

        // A NextPage section break behaves like a page break: the following content starts a fresh page.
        await Assert.That(document.Pages.Count).IsEqualTo(2);
        await Assert.That(document.Pages[1].Items.OfType<PlacedLine>().SelectMany(_ => _.Runs).Any(_ => _.Text == "second")).IsTrue();
    }

    [Test]
    public async Task A_continuous_section_break_keeps_content_on_the_same_page()
    {
        var document = fragmenter.Layout(
            [
                P("first"), new SectionBreakElement
                {
                    BreakType = SectionBreakType.Continuous
                },
                P("second")
            ],
            Page(400));

        // A continuous section break at the same geometry takes no flow space — both paragraphs stay on page 1.
        await Assert.That(document.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_next_page_section_break_switches_page_geometry()
    {
        var second = new PageSettings
        {
            WidthPoints = 400,
            HeightPoints = 300,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20
        };
        var document = fragmenter.Layout(
            [
                P("first"), new SectionBreakElement
                {
                    BreakType = SectionBreakType.NextPage,
                    NewSectionSettings = second
                },
                P("second")
            ],
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
        var document = fragmenter.Layout(
            [
                P("first"), new SectionBreakElement
                {
                    BreakType = SectionBreakType.OddPage
                },
                P("second")
            ],
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
        var twoColumns = new PageSettings
        {
            WidthPoints = 300,
            HeightPoints = 120,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20,
            ColumnCount = 2,
            ColumnSpacing = 20
        };
        var body = Enumerable.Range(0, 12).Select(_ => P("body")).ToArray();
        var document = fragmenter.Layout(
            [
                P("MASTHEAD"), new SectionBreakElement
                {
                    BreakType = SectionBreakType.Continuous,
                    NewSectionSettings = twoColumns
                },
                .. body
            ],
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
        var twoColumns = new PageSettings
        {
            WidthPoints = 300,
            HeightPoints = 120,
            MarginTop = 20,
            MarginBottom = 20,
            MarginLeft = 20,
            MarginRight = 20,
            ColumnCount = 2,
            ColumnSpacing = 20
        };
        var body = Enumerable.Range(0, 40).Select(_ => P("body")).ToArray();
        var document = fragmenter.Layout(
            [
                P("MASTHEAD"), new SectionBreakElement
                {
                    BreakType = SectionBreakType.Continuous,
                    NewSectionSettings = twoColumns
                },
                .. body
            ],
            Page(120));

        await Assert.That(document.Pages.Count > 1).IsTrue();
        var page2Lines = document.Pages[1].Items.OfType<PlacedLine>().ToList();
        await Assert.That(page2Lines.Min(_ => _.Y)).IsEqualTo(20f).Within(0.5f);
    }

    // A three-column geometry with columns at x=0, 156, 312 (468pt wide, no margins/spacing).
    static PageSettings ThreeColumnSheet(int columns) =>
        new()
        {
            WidthPoints = 468,
            HeightPoints = 600,
            MarginTop = 0,
            MarginBottom = 0,
            MarginLeft = 0,
            MarginRight = 0,
            ColumnCount = columns,
            ColumnSpacing = 0
        };

    [Test]
    public async Task A_multi_column_section_terminated_by_a_break_balances_its_columns()
    {
        // Six short items in a three-column section, then a continuous break to one column. Word balances the
        // terminated section's columns to equal heights — two items each — rather than newspaper-filling
        // column 0 (which, on a 600pt-tall page, would hold all six).
        var items = Enumerable.Range(1, 6).Select(_ => P($"Item {_}")).ToArray();
        var document = fragmenter.Layout(
            [
                .. items, new SectionBreakElement
                {
                    BreakType = SectionBreakType.Continuous,
                    NewSectionSettings = ThreeColumnSheet(1)
                },
                P("footer")
            ],
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
        var document = fragmenter.Layout([.. items], ThreeColumnSheet(3));

        var itemLines = document.Pages[0].Items.OfType<PlacedLine>().Where(_ => _.Runs.Any(run => run.Text.StartsWith("Item"))).ToList();
        await Assert.That(itemLines.Count).IsEqualTo(6);
        await Assert.That(itemLines.All(_ => _.X < 100f)).IsTrue();
    }

    static FloatingImageElement Float(WrapType wrap, double width, double height, WrapTextSide side = WrapTextSide.BothSides) =>
        new()
        {
            ImageData = pixel,
            ContentType = "image/png",
            WidthPoints = width,
            HeightPoints = height,
            WrapType = wrap,
            WrapTextSide = side,
            HorizontalAnchor = HorizontalAnchor.Column,
            VerticalAnchor = VerticalAnchor.Paragraph
        };

    [Test]
    public async Task A_wrapping_float_narrows_the_paragraphs_beside_it()
    {
        // A 100pt-wide square float at the column's left edge over a 260pt measure. Text beside it must
        // start past the float and wrap to the ~160pt that remains, not the full measure.
        const string text = "The quick brown fox jumps over the lazy dog again and again and again";
        var withFloat = fragmenter.Layout([Float(WrapType.Square, 100, 60), P(text)], Page(400));
        var without = fragmenter.Layout([P(text)], Page(400));

        var banded = withFloat.Pages[0].Items.OfType<PlacedLine>().ToList();
        var full = without.Pages[0].Items.OfType<PlacedLine>().ToList();

        // Pushed clear of the float horizontally, and narrower — so it takes more lines than unobstructed.
        await Assert.That(banded[0].X).IsGreaterThanOrEqualTo(100f);
        await Assert.That(banded.Count).IsGreaterThan(full.Count);
    }

    [Test]
    public async Task A_wrap_none_float_leaves_the_measure_alone()
    {
        // wrapNone (and behind-text) floats overlap the text by design: no exclusion, no narrowing.
        const string text = "The quick brown fox jumps over the lazy dog again and again and again";
        var withFloat = fragmenter.Layout([Float(WrapType.None, 100, 60), P(text)], Page(400));
        var without = fragmenter.Layout([P(text)], Page(400));

        var banded = withFloat.Pages[0].Items.OfType<PlacedLine>().ToList();
        var full = without.Pages[0].Items.OfType<PlacedLine>().ToList();
        await Assert.That(banded.Count).IsEqualTo(full.Count);
        await Assert.That(banded[0].X).IsEqualTo(full[0].X).Within(0.01f);
    }

    [Test]
    public async Task A_top_and_bottom_float_pushes_the_text_below_it()
    {
        // wrapTopAndBottom takes the whole measure, so nothing sits beside it — the text starts under it.
        var document = fragmenter.Layout([Float(WrapType.TopAndBottom, 100, 60), P("Below")], Page(400));
        var line = document.Pages[0].Items.OfType<PlacedLine>().First();
        await Assert.That(line.Y).IsGreaterThanOrEqualTo(60f);
    }
}
