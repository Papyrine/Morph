/// <summary>
/// Guards the two Word-matching table row-height rules in <see cref="TableHeightCalculator"/>:
/// (1) every paragraph's space-after — including the last one's — STACKS on the bottom cell
/// margin (sum, not max); and
/// (2) the border-collapse pass grows the first/last row by the table's OUTER horizontal borders.
///
/// Both are asserted in spacing units against a stub measurer so they stay meaningful regardless
/// of font rasterisation. The stacking rule is measured from Word's own render of
/// table_default_style (tblCellMar 3pt/3pt, Normal 8pt-after, one 12pt line -> Word row
/// 31pt = 3 + line + 8 + 3): an earlier max-overlap rule predicted 28pt and rendered rows
/// visibly shorter than Word. That overlap dated from when cell line heights ran too small and
/// a 2pt default-padding fudge inflated rows — the rules were calibrated against each other.
/// </summary>
public class TableRowHeightRulesTests
{
    static TableCell CellWithAfter(double spacingAfter) =>
        new()
        {
            Content =
            [
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = "x",
                            Properties = new()
                        }
                    ],
                    Properties = new()
                    {
                        SpacingAfterPoints = spacingAfter
                    }
                }
            ],
            Properties = new()
        };

    [Test]
    public async Task TrailingAfterSpacing_StacksOnBottomMargin()
    {
        // bottom margin 5.75, after 8 → both count in full: the after does not collapse into
        // the margin (Word truth measured on table_default_style — see class doc).
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 5.75, right: 0, bottom: 5.75, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(CellWithAfter(8), cellWidth: 100, tableProps, new StubMeasurer());

        // padding.Vertical (11.5) + one stub line (12) + after (8) = 31.5.
        await Assert.That(height).IsEqualTo(31.5f).Within(0.01f);
    }

    [Test]
    public async Task TrailingAfterSpacing_StacksEvenWhenMarginExceedsAfter()
    {
        // bottom margin 10 > after 8 → still summed, not absorbed.
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 4, right: 0, bottom: 10, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(CellWithAfter(8), cellWidth: 100, tableProps, new StubMeasurer());

        // padding.Vertical (14) + line (12) + after (8) = 34.
        await Assert.That(height).IsEqualTo(34f).Within(0.01f);
    }

    [Test]
    public async Task InterParagraphAfterSpacing_IsNotOverlapped()
    {
        // Two paragraphs: each paragraph's after-spacing is added in full — the first forms the
        // inter-paragraph gap, the last stacks on the (here zero) bottom margin.
        var cell = new TableCell
        {
            Content =
            [
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = "a",
                            Properties = new()
                        }
                    ],
                    Properties = new()
                    {
                        SpacingAfterPoints = 8
                    }
                },
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = "b",
                            Properties = new()
                        }
                    ],
                    Properties = new()
                    {
                        SpacingAfterPoints = 8
                    }
                }
            ],
            Properties = new()
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // line(12) + first-after(8, full) + line(12) + max(0, 8 - 0)=8 = 40.
        await Assert.That(height).IsEqualTo(40f).Within(0.01f);
    }

    [Test]
    public async Task ContextualSpacing_CollapsesTheGapBetweenSameStyleParagraphs()
    {
        // w:contextualSpacing suppresses the gap between same-style contextual paragraphs, so the
        // row must be sized without it — the placement path (Fragmenter's cell arm) draws them
        // tight, and measuring the gaps anyway left the row that much too tall with the surplus
        // as dead space below the content. Four 18pt-after paragraphs: three boundaries collapse
        // (3 x 18 = 54pt saved), while the LAST paragraph's after still stacks on the bottom
        // margin. This is letters/04's RecipientAddress block to the point.
        var cell = new TableCell
        {
            Content =
            [
                ContextualParagraph("a"),
                ContextualParagraph("b"),
                ContextualParagraph("c"),
                ContextualParagraph("d")
            ],
            Properties = new()
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // 4 lines (48) + trailing after (18) = 66; the three inter-paragraph gaps cost nothing.
        await Assert.That(height).IsEqualTo(66f).Within(0.01f);
    }

    [Test]
    public async Task ContextualSpacing_DoesNotCollapseAcrossDifferentStyles()
    {
        // The collapse is same-style only: a contextual paragraph followed by a contextual one of
        // another style still pays the gap, matching the placement path's StyleId test.
        var cell = new TableCell
        {
            Content =
            [
                ContextualParagraph("a"),
                ContextualParagraph("b", styleId: "Other")
            ],
            Properties = new()
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // line(12) + first-after(18) + line(12) + trailing after(18) = 60.
        await Assert.That(height).IsEqualTo(60f).Within(0.01f);
    }

    [Test]
    public async Task ContextualSpacing_DoesNotCollapseAcrossInterveningContent()
    {
        // w:contextualSpacing suppresses spacing only against the immediately preceding/following
        // PARAGRAPH. A drawing between two same-style contextual paragraphs separates them, so the
        // gap is still paid — wedding/08's cell is Title, inline oval, Title, and collapsing across
        // the oval pulled the second title and everything below it up the page.
        var cell = new TableCell
        {
            Content =
            [
                ContextualParagraph("a"),
                new WordArtElement
                {
                    Text = "art",
                    HeightPoints = 20,
                    WidthPoints = 20
                },
                ContextualParagraph("b")
            ],
            Properties = new()
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // wordArt(20) + line(12) + first-after(18) + line(12) + trailing after(18) = 80.
        await Assert.That(height).IsEqualTo(80f).Within(0.01f);
    }

    // A paragraph opts out of only its OWN half of a gap (ParagraphGap, Word-probed in cells too:
    // _probe_ctx's table row). After 30 then before 20 leaves nothing when the first opts in and 30
    // when only the second does, and the row is sized to match.
    [Test]
    [Arguments(true, false, 0)]
    [Arguments(false, true, 30)]
    [Arguments(false, false, 30)]
    [Arguments(true, true, 0)]
    public async Task ContextualSpacing_DropsOnlyTheHalfOfTheParagraphThatOptsIn(bool firstContextual, bool secondContextual, int gap)
    {
        static ParagraphElement Paragraph(double before, double after, bool contextual) =>
            new()
            {
                Runs = [new() {Text = "a", Properties = new()}],
                Properties = new()
                {
                    StyleId = "S",
                    ContextualSpacing = contextual,
                    SpacingBeforePoints = before,
                    SpacingAfterPoints = after
                }
            };

        var cell = new TableCell
        {
            Content =
            [
                Paragraph(0, 30, firstContextual),
                Paragraph(20, 0, secondContextual)
            ],
            Properties = new()
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // Two 12pt lines and the gap between them; the last paragraph leaves nothing after it.
        await Assert.That(height).IsEqualTo(24f + gap).Within(0.01f);
    }

    static ParagraphElement ContextualParagraph(string text, string styleId = "RecipientAddress") =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = text,
                    Properties = new()
                }
            ],
            Properties = new()
            {
                StyleId = styleId,
                ContextualSpacing = true,
                SpacingAfterPoints = 18
            }
        };

    [Test]
    public async Task BorderCollapse_OuterBordersGrowSingleRow()
    {
        var cell = new TableCell
        {
            Properties = new()
            {
                Borders = new()
                {
                    Top = new()
                    {
                        IsVisible = true,
                        WidthPoints = 1
                    },
                    Bottom = new()
                    {
                        IsVisible = true,
                        WidthPoints = 1
                    }
                }
            },
            Content =
            [
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = "x",
                            Properties = new()
                        }
                    ],
                    Properties = new()
                }
            ]
        };
        var table = new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells = [cell]
                }
            ]
        };

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f], new StubMeasurer());

        // The single row is both first and last, so it accrues BOTH outer edges. There is no
        // artificial row-height floor (a blanket minimum balloons short rows), so the box is
        // content(line 12) + top(1) + bottom(1) = 14.
        await Assert.That(heights[0]).IsEqualTo(14f).Within(0.01f);
    }

    [Test]
    public async Task BorderCollapse_NoBorders_AddsNothing()
    {
        var cell = new TableCell
        {
            Properties = new(),
            Content =
            [
                new ParagraphElement
                {
                    Runs =
                    [
                        new()
                        {
                            Text = "x",
                            Properties = new()
                        }
                    ],
                    Properties = new()
                }
            ]
        };
        var table = new TableElement
        {
            Rows =
            [
                new()
                {
                    Cells = [cell]
                }
            ]
        };

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f], new StubMeasurer());

        // No borders → no growth; the row is just the measured content (one 12pt stub line), with
        // no artificial minimum-height floor.
        await Assert.That(heights[0]).IsEqualTo(12f).Within(0.01f);
    }

    // A merge takes its bottom margin from its LAST row (_probe_vmergemar): eight 12pt lines merged
    // down two one-line rows end 40pt lower when the last row's plain cell has a 40pt bottom margin,
    // and not at all when the head row's does (that row grows by it, the merge does not).
    [Test]
    [Arguments(false, 12f, 124f)]
    [Arguments(true, 52f, 44f)]
    public async Task A_merge_takes_its_bottom_margin_from_its_last_row(bool onHead, float head, float last)
    {
        static TableCell Plain(double bottom) =>
            new()
            {
                Content = [new ParagraphElement { Runs = [new() { Text = "x", Properties = new() }], Properties = new() }],
                Properties = new() { Padding = new(0, 0, bottom, 0) }
            };

        var merged = new TableCell
        {
            Content = [.. Enumerable.Range(0, 8).Select(_ => new ParagraphElement { Runs = [new() { Text = "line", Properties = new() }], Properties = new() })],
            Properties = new() { VerticalMerge = VerticalMergeType.Restart }
        };
        var table = new TableElement
        {
            Rows =
            [
                new() { Cells = [Plain(onHead ? 40 : 0), merged] },
                new() { Cells = [Plain(onHead ? 0 : 40), new() { Content = [new ParagraphElement { Runs = [] }], Properties = new() { VerticalMerge = VerticalMergeType.Continue } }] }
            ]
        };

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f, 100f], new StubMeasurer());

        await Assert.That(heights[0]).IsEqualTo(head).Within(0.01f);
        await Assert.That(heights[1]).IsEqualTo(last).Within(0.01f);
    }

    // A WordArt box's paragraph keeps its after-spacing in a cell (wedding/08's ampersand badge between
    // two contextual Titles): 12 + 8 after, the 50pt box + its own 8 after, 12 + 8 after.
    [Test]
    public async Task A_wordart_in_a_cell_keeps_its_paragraphs_after_spacing()
    {
        static ParagraphElement Title() =>
            new()
            {
                Runs = [new() { Text = "Title", Properties = new() }],
                Properties = new() { StyleId = "Title", ContextualSpacing = true, SpacingAfterPoints = 8 }
            };

        var cell = new TableCell
        {
            Content = [Title(), new WordArtElement { Text = "&", WidthPoints = 50, HeightPoints = 50, SpacingAfterPoints = 8 }, Title()],
            Properties = new()
        };
        var table = new TableElement { Rows = [new() { Cells = [cell] }] };

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f], new StubMeasurer());

        await Assert.That(heights[0]).IsEqualTo(12 + 8 + 50 + 8 + 12 + 8f).Within(0.01f);
    }

    sealed class StubMeasurer : IParagraphMeasurer
    {
        public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth) => [12f];
        public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) => 12f;
        public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth) => 50f;
    }
}
