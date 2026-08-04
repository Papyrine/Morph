/// <summary>
/// Guards the inter-paragraph spacing collapse inside table cells:
/// <see cref="TableHeightCalculator.MeasureCellHeight"/> charges max(after, before) between
/// consecutive paragraphs, not the sum (verified against Word XPS baselines). Asserted against the
/// canonical measurer — the one measurement model since the production renderers were deleted; the
/// engine's cell content is placed from the same measurement, so measure/render agreement holds by
/// construction.
/// </summary>
public class TableCellSpacingCollapseTests
{
    const float spacingBefore = 10;
    const float spacingAfter = 10;

    static ParagraphElement Paragraph(string text) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new()}],
            Properties = new()
            {
                SpacingBeforePoints = spacingBefore,
                SpacingAfterPoints = spacingAfter
            }
        };

    static TableCell Cell(params string[] texts) =>
        new()
        {
            Properties = new(),
            Content = [..texts.Select(Paragraph)]
        };

    // No padding/margin so the measured height is purely the paragraph contributions.
    static TableProperties TableProps() =>
        new()
        {
            DefaultCellPadding = new(top: 0, right: 0, bottom: 0, left: 0)
        };

    [Test]
    public async Task MultiParagraphCell_CollapsesSpacingBetweenParagraphs()
    {
        const float width = 300;
        var one = TableHeightCalculator.MeasureCellHeight(Cell("Paragraph 0"), width, TableProps(), LayoutTestFonts.Measurer);
        var three = TableHeightCalculator.MeasureCellHeight(Cell("Paragraph 0", "Paragraph 1", "Paragraph 2"), width, TableProps(), LayoutTestFonts.Measurer);

        // Word charges max(after, before) between consecutive paragraphs, not the sum. With
        // before == after, each extra paragraph adds its lines plus exactly one gap. Summing both
        // would add spacingBefore extra per gap — the defect this guards.
        var perExtraParagraph = (three - one) / 2;
        var oneLineAndOneGap = (one - spacingBefore - spacingAfter) + spacingAfter;

        await Assert.That(perExtraParagraph).IsEqualTo(oneLineAndOneGap).Within(0.01f);
    }
}
