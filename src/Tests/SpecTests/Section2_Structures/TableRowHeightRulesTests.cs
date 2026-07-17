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

        // The single row is both first and last, so it accrues BOTH outer edges. There is no
        // artificial row-height floor (a blanket 20pt minimum was removed — it ballooned short
        // rows), so the box is content(line 12) + top(1) + bottom(1) = 14.
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

        var heights = TableHeightCalculator.CalculateRowHeights(table, [100f], new StubMeasurer(), hasVerticalMerge: false);

        // No borders → no growth; the row is just the measured content (one 12pt stub line), with
        // no artificial minimum-height floor.
        await Assert.That(heights[0]).IsEqualTo(12f).Within(0.01f);
    }

    sealed class StubMeasurer : IParagraphMeasurer
    {
        public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth) => [12f];
        public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) => 12f;
        public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth) => 50f;
    }
}
