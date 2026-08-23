/// <summary>
/// Covers content-based autofit (<see cref="TableLayout.CalculateColumnWidths"/>) for
/// tables that arrive with no usable column widths — bare <c>w:gridCol/></c> entries
/// and no per-cell <c>w:tcW</c>. With autofit on, columns are sized by content;
/// with fixed layout (or no measurer), columns fall back to equal divide.
///
/// <para>Also covers the SEEDED form of the same pass: when every column carries an explicit
/// <c>w:tcW</c>, those widths are the preferred seed and <c>w:tblGrid</c> is ignored as the
/// stale cache it is. Content may only widen a column past its declared width, never narrow it
/// below its longest unbreakable token.</para>
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
        // No grid to be stale against, so the explicit-widths branch handles scaling/grow as it
        // always has. Sanity-check that adding a measurer doesn't change this behaviour.
        var table = MakeTable(
            isAutoFit: true,
            cellWidths: [[50, 150]],
            ["a", "b"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        await Assert.That(widths[0]).IsEqualTo(50).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(150).Within(0.001f);
    }

    [Test]
    public async Task CalculateColumnWidths_DeclaredCellWidths_BeatStaleGrid()
    {
        // w:tblGrid is the cache of Word's last layout; w:tcW is the input. A generator that
        // writes cell widths without recomputing the grid leaves the two disagreeing, and Word
        // lays the table out at the cell widths — see "Stocktake export template v2.docx", whose
        // Name column declares 6374tw against the grid's 1849tw.
        var table = MakeTable(
            isAutoFit: true,
            cellWidths: [[150, 50]],
            gridWidths: [100, 100],
            preferredTableWidth: null,
            ["a", "b"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        await Assert.That(widths[0]).IsEqualTo(150).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(50).Within(0.001f);
    }

    [Test]
    public async Task CalculateColumnWidths_DeclaredCellWidths_GrowForUnbreakableContent()
    {
        // The declared width is a preference, not a floor-and-ceiling: a column holding a token
        // that cannot be broken grows to fit it, and the other columns give the space back in
        // proportion to their own slack. Word does exactly this to the Challenges column of the
        // stocktake table — declared 60.5pt, laid out at 149.3pt to fit
        // "Legislative/regulatory/constitutional" — while the total stays pinned to w:tblW.
        var unbreakable = new string('x', 60);
        var table = MakeTable(
            isAutoFit: true,
            cellWidths: [[180, 20]],
            gridWidths: [100, 100],
            preferredTableWidth: 200,
            ["a b c", unbreakable]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 400, measurer);

        // Fitted to w:tblW rather than to the whole text column.
        await Assert.That(widths.Sum()).IsEqualTo(200).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(60).Within(0.001f);
        await Assert.That(widths[0]).IsLessThan(180);
    }

    [Test]
    public async Task CalculateColumnWidths_GridAgreeingWithCells_IsNotRefitted()
    {
        // The common case, and the one that must not move: Word wrote the grid out of its own
        // fitted layout, so when it agrees with the cells it is the better number and content
        // must not be allowed to push the columns around. newsletters/05 is the corpus proof —
        // grid and w:tcW identical, and re-fitting it moved the body column 8pt off Word.
        var table = MakeTable(
            isAutoFit: true,
            cellWidths: [[100, 100]],
            gridWidths: [100, 100],
            preferredTableWidth: 200,
            ["a b c", new('x', 60)]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        await Assert.That(widths[0]).IsEqualTo(100).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(100).Within(0.001f);
    }

    [Test]
    public async Task CalculateColumnWidths_GridDifferingOnlyByUniformScale_IsNotRefitted()
    {
        // A grid and a cell set that differ by a single scale factor describe the SAME shape —
        // one side carries a w:tblW target the other doesn't. labels/13 (11376 grid vs 11724
        // cells) and cards/* (11520 vs 10800) are this, and none of them is stale.
        var table = MakeTable(
            isAutoFit: true,
            cellWidths: [[75, 225]],
            gridWidths: [50, 150],
            preferredTableWidth: null,
            ["a", "b"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        // Grid shape retained, grown to the container as the grid branch has always done.
        await Assert.That(widths[0] / widths[1]).IsEqualTo(50f / 150).Within(0.001f);
    }

    [Test]
    public async Task CalculateColumnWidths_FixedLayout_KeepsGridOverDeclaredCellWidths()
    {
        // Fixed-layout tables are sized verbatim, never fitted, so the grid stays authoritative
        // for them — header_full_bleed_banner's 625.4pt banner grid depends on it.
        var table = MakeTable(
            isAutoFit: false,
            cellWidths: [[150, 50]],
            gridWidths: [100, 100],
            preferredTableWidth: null,
            ["a", "b"]);

        var measurer = new ProportionalMeasurer();
        var widths = TableLayout.CalculateColumnWidths(table, colCount: 2, availableWidth: 200, measurer);

        await Assert.That(widths[0]).IsEqualTo(100).Within(0.001f);
        await Assert.That(widths[1]).IsEqualTo(100).Within(0.001f);
    }

    static TableElement MakeTable(params string[][] rowsContent) =>
        MakeTable(isAutoFit: true, cellWidths: null, rowsContent);

    static TableElement MakeTable(bool isAutoFit, double?[][]? cellWidths, params string[][] rowsContent) =>
        MakeTable(isAutoFit, cellWidths, gridWidths: null, preferredTableWidth: null, rowsContent);

    static TableElement MakeTable(bool isAutoFit, double?[][]? cellWidths, List<double>? gridWidths, double? preferredTableWidth, params string[][] rowsContent)
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
                IsAutoFit = isAutoFit,
                GridColumnWidths = gridWidths,
                PreferredWidthPoints = preferredTableWidth
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
