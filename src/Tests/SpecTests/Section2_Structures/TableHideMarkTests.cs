/// <summary>
/// Covers <c>w:hideMark</c> — when set, the end-of-cell paragraph mark is suppressed
/// for height measurement so an empty cell can collapse below one line of text.
/// Cell with non-empty content ignores the flag.
/// </summary>
public class TableHideMarkTests
{
    [Test]
    public async Task TableCellProperties_DefaultHideMark_IsFalse()
    {
        var props = new TableCellProperties();

        await Assert.That(props.HideMark).IsFalse();
    }

    [Test]
    public async Task MeasureCellHeight_HideMarkOnEmptyCell_CollapsesToPaddingOnly()
    {
        var cell = new TableCell
        {
            Properties = new() { HideMark = true },
            Content = [new ParagraphElement { Runs = [], Properties = new() }]
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 5, right: 5, bottom: 5, left: 5)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // Padding only — no paragraph contribution.
        await Assert.That(height).IsEqualTo(10f);
    }

    [Test]
    public async Task MeasureCellHeight_HideMarkOnEmptyCell_DoesNotCollapseWhenContentExists()
    {
        var cell = new TableCell
        {
            Properties = new()
            {
                HideMark = true
            },
            Content =
            [
                new ParagraphElement
                {
                    Runs = [
                        new()
                        {
                            Text = "x",
                            Properties = new()
                        }],
                    Properties = new()
                }
            ]
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 5, right: 5, bottom: 5, left: 5)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // Padding (10) + one stub line (12) = 22.
        await Assert.That(height).IsEqualTo(22f);
    }

    [Test]
    public async Task MeasureCellHeight_NoHideMark_OnEmptyCell_StillReservesLineHeight()
    {
        var cell = new TableCell
        {
            Properties = new(),
            Content = [new ParagraphElement { Runs = [], Properties = new() }]
        };
        var tableProps = new TableProperties
        {
            DefaultCellPadding = new(top: 5, right: 5, bottom: 5, left: 5)
        };

        var height = TableHeightCalculator.MeasureCellHeight(cell, cellWidth: 100, tableProps, new StubMeasurer());

        // Padding (10) + empty paragraph line (12) = 22 — the end-of-cell mark still counts.
        await Assert.That(height).IsEqualTo(22f);
    }

    sealed class StubMeasurer : IParagraphMeasurer
    {
        public List<float> LayoutParagraphForMeasurement(ParagraphElement paragraph, float maxWidth) => [12f];
        public float MeasureParagraphHeightWithWidth(ParagraphElement paragraph, float maxWidth) => 12f;
        public float MeasureParagraphNaturalWidth(ParagraphElement paragraph, float maxWidth) => 50f;
    }
}
