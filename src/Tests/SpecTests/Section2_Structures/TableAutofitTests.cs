/// <summary>
/// Covers content-based autofit (<see cref="TableLayout.CalculateColumnWidths"/>) for
/// tables that arrive with no usable column widths — bare <c>w:gridCol/></c> entries
/// and no per-cell <c>w:tcW</c>. With autofit on, columns are sized by content;
/// with fixed layout (or no measurer), columns fall back to equal divide.
/// </summary>
public class TableAutofitTests
{
    [Test]
    public async Task CalculateColumnWidths_ContentAutofit_HugsContentWhenItFits()
    {
        // Two columns, autofit, no widths supplied. With ample available width and short
        // content, Word leaves the table at its preferred (content) width rather than
        // growing to fill the page — see wide_table scenario.
        var table = MakeTable(
            ["x", "y"],
            ["longer content here", "y"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 400, measurer);

        await Assert.That(widths.Sum()).IsLessThan(400);
        await Assert.That(widths[0]).IsGreaterThan(widths[1]);
    }

    [Test]
    public async Task CalculateColumnWidths_ContentAutofit_RespectsMinWidthWhenPrefOverflows()
    {
        // Preferred widths overflow available: result interpolates between min and pref.
        // Min for both cells is the longest token; the right cell has a much longer token,
        // so its column should still get the larger share. availableWidth is chosen so
        // sumMin < avail < sumPref to exercise the interpolation branch.
        var table = MakeTable(
            ["ab cd ef", "supercalifragilisticexpialidocious"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 38, measurer);

        await Assert.That(widths.Sum()).IsEqualTo(38).Within(0.1f);
        await Assert.That(widths[1]).IsGreaterThan(widths[0]);
    }

    [Test]
    public async Task CalculateColumnWidths_ContentAutofit_ScalesDownWhenMinExceedsAvailable()
    {
        // Even the longest unbreakable tokens won't fit: we scale the min widths down
        // to fill exactly. This is the "page edge wins" fallback.
        var table = MakeTable(
            ["aaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbb"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 10, measurer);

        await Assert.That(widths.Sum()).IsEqualTo(10).Within(0.1f);
    }

    [Test]
    public async Task CalculateColumnWidths_NoMeasurer_FallsBackToEqualDivide()
    {
        // Even with autofit, omitting the measurer must keep the legacy equal-divide
        // behaviour so existing call paths without measurer access stay deterministic.
        var table = MakeTable(
            ["a", "loooooong"]);

        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer: null);

        await Assert.That(widths[0]).IsEqualTo(100).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(100).Within(0.001f);
    }

    [Test]
    public async Task CalculateColumnWidths_FixedLayout_IgnoresContentAndDividesEqually()
    {
        // Fixed layout shouldn't autofit even with a measurer present. Word's behaviour:
        // fixed-layout tables honour widths verbatim and ignore content for column sizing.
        var table = MakeTable(
            isAutoFit: false,
            cellWidths: null,
            ["a", "loooooong"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        await Assert.That(widths[0]).IsEqualTo(100).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(100).Within(0.001f);
    }

    [Test]
    public async Task CalculateColumnWidths_ExplicitCellWidths_TakePrecedenceOverAutofit()
    {
        // When per-cell widths are present, the autofit branch isn't entered — the
        // explicit-widths branch handles scaling/grow. Sanity-check that adding a measurer
        // doesn't change this behaviour.
        var table = MakeTable(
            isAutoFit: true,
            cellWidths: [[50, 150]],
            ["a", "b"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        await Assert.That(widths[0]).IsEqualTo(50).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(150).Within(0.001f);
    }

    static TableElement MakeTable(params string[][] rowsContent) =>
        MakeTable(isAutoFit: true, cellWidths: null, rowsContent);

    static TableElement MakeTable(bool isAutoFit, double?[][]? cellWidths, params string[][] rowsContent)
    {
        var rows = new List<TableRow>();
        for (var rowIndex = 0; rowIndex < rowsContent.Length; rowIndex++)
        {
            var rowContent = rowsContent[rowIndex];
            var cells = new List<TableCell>();
            for (var colIndex = 0; colIndex < rowContent.Length; colIndex++)
            {
                var text = rowContent[colIndex];
                var width = cellWidths?[rowIndex][colIndex];
                cells.Add(
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
                                        Text = text,
                                        Properties = new()
                                    }
                                ],
                                Properties = new()
                            }
                        ],
                        Properties = new()
                        {
                            WidthPoints = width
                        }
                    });
            }

            rows.Add(new()
            {
                Cells = cells
            });
        }

        return new()
        {
            Rows = rows,
            Properties = new()
            {
                IsAutoFit = isAutoFit
            }
        };
    }

    /// <summary>
    /// Backend-free measurer for unit tests: paragraph width = sum of run text lengths,
    /// with whitespace acting as a wrap point. Pass a tiny maxWidth to get the longest
    /// unbreakable token's "width" (its character count); pass a large maxWidth to get
    /// the natural single-line width.
    /// </summary>
    sealed class ProportionalMeasurer : IParagraphMeasurer
    {
        public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth) =>
            [paragraph.Runs.Sum(_ => (float)_.Text.Length)];

        public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth)
        {
            var widest = 0f;
            var current = 0f;
            foreach (var run in paragraph.Runs)
            {
                foreach (var token in run.Text.Split(' '))
                {
                    var tokenLen = (float)token.Length;
                    if (current > 0 && current + 1 + tokenLen > maxWidth)
                    {
                        if (current > widest)
                        {
                            widest = current;
                        }

                        current = tokenLen;
                    }
                    else
                    {
                        current = current > 0 ? current + 1 + tokenLen : tokenLen;
                    }
                }
            }

            return Math.Max(widest, current);
        }

        public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) => 12;
    }
}
