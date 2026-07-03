/// <summary>
/// Guards the two Word-matching table row-height rules in <see cref="TableHeightCalculator"/>:
/// (1) the last paragraph's space-after OVERLAPS the bottom cell margin (max, not sum); and
/// (2) the border-collapse pass grows the first/last row by the table's OUTER horizontal borders.
///
/// Both are asserted in spacing units against a stub measurer so they stay meaningful regardless
/// of font rasterisation. Regression guard for the bug where the trailing after-spacing stacked on
/// the bottom margin (agendas-minutes/01 schedule rows rendered ~5pt/row taller than Word, with
/// top-aligned content pushed up) and bordered tables ignored their border widths.
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
    public async Task TrailingAfterSpacing_OverlapsBottomMargin_WhenAfterExceedsMargin()
    {
        // bottom margin 5.75 < after 8 → bottom region is max(8, 5.75) = 8, i.e. the after sticks out
        // 8 - 5.75 = 2.25 past the margin. Stacking would instead add the full 8 on top of the margin.
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 5.75, right: 0, bottom: 5.75, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(CellWithAfter(8), cellWidth: 100, tableProps, new StubMeasurer());

        // padding.Vertical (11.5) + one stub line (12) + max(0, 8 - 5.75) = 25.75 (NOT 31.5 = stacked).
        await Assert.That(height).IsEqualTo(25.75f).Within(0.01f);
    }

    [Test]
    public async Task TrailingAfterSpacing_FullyAbsorbed_WhenMarginExceedsAfter()
    {
        // bottom margin 10 >= after 8 → the after is entirely absorbed by the margin (contributes 0).
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 4, right: 0, bottom: 10, left: 0)
        };

        var height = TableHeightCalculator.MeasureCellHeight(CellWithAfter(8), cellWidth: 100, tableProps, new StubMeasurer());

        // padding.Vertical (14) + line (12) + max(0, 8 - 10) = 26 (NOT 34 = stacked).
        await Assert.That(height).IsEqualTo(26f).Within(0.01f);
    }

    [Test]
    public async Task InterParagraphAfterSpacing_IsNotOverlapped()
    {
        // Two paragraphs: the FIRST paragraph's after-spacing forms the inter-paragraph gap and is
        // added in full; only the LAST paragraph's after overlaps the bottom margin.
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

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f], new StubMeasurer(), hasVerticalMerge: false);

        // The single row is both first and last, so it accrues BOTH outer edges. Content (line 12)
        // is below the 20pt minimum row height, so the box is 20 + top(1) + bottom(1) = 22.
        await Assert.That(heights[0]).IsEqualTo(22f).Within(0.01f);
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

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f], new StubMeasurer(), hasVerticalMerge: false);

        // No borders → no growth; stays at the 20pt minimum row height.
        await Assert.That(heights[0]).IsEqualTo(20f).Within(0.01f);
    }

    sealed class StubMeasurer : IParagraphMeasurer
    {
        public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth) => [12f];
        public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) => 12f;
        public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth) => 50f;
    }
}
