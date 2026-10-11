/// <summary>
/// Where a WordArt box stands in the flow and in a table cell — the spacing and alignment of the paragraph
/// it replaces travel with it.
/// </summary>
internal class WordArtPlacementTests
{
    static readonly PageSettings page = new() {WidthPoints = 400, HeightPoints = 400, MarginTop = 40, MarginBottom = 40, MarginLeft = 40, MarginRight = 40};

    static WordArtElement Box(double height, double after, WordArtTransform transform = WordArtTransform.Wave, TextAlignment alignment = TextAlignment.Left) => new()
    {
        Text = "Wave",
        WidthPoints = 100,
        HeightPoints = height,
        FontFamily = "Arial",
        Transform = transform,
        SpacingAfterPoints = after,
        Alignment = alignment
    };

    // wordart's wave is the first thing on its page, carried there because it did not fit under the circle
    // before it; the chevron under it stood flush against it, 10pt (the wave paragraph's after) high of
    // Word's, and every box below with it.
    [Test]
    public async Task A_box_carried_to_a_fresh_page_keeps_its_after_spacing()
    {
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Box(200, 10), Box(200, 10), Box(60, 10)], page);

        await Assert.That(laidOut.Pages.Count).IsEqualTo(2);
        var carried = laidOut.Pages[1].Items.OfType<PlacedWordArt>().ToList();
        await Assert.That(carried.Count).IsEqualTo(2);
        await Assert.That(carried[0].Y).IsEqualTo(40f).Within(0.01f);
        await Assert.That(carried[1].Y).IsEqualTo(40f + 200 + 10).Within(0.01f);
    }

    // wedding/08's ampersand badge: an unwarped box in a centred paragraph of a cell. Word centres it
    // under the names; set at the cell's left edge it stood 43pt left of them.
    [Test]
    [Arguments(TextAlignment.Left, 0f)]
    [Arguments(TextAlignment.Center, 0.5f)]
    [Arguments(TextAlignment.Right, 1f)]
    public async Task A_box_in_a_cell_stands_where_its_paragraphs_alignment_puts_it(TextAlignment alignment, float shareOfSlack)
    {
        var table = new TableElement
        {
            Rows = [new() {Cells = [new() {Properties = new() {Padding = new()}, Content = [Box(30, 0, alignment: alignment)]}]}],
            Properties = new() {GridColumnWidths = [260], DefaultCellPadding = new()}
        };

        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([table], page);
        var cell = laidOut.Pages[0].Items.OfType<PlacedTableRow>().Single().Cells[0];
        var box = cell.Content.OfType<PlacedWordArt>().Single();

        // The cell's whole width is content here (no padding), and the box is 100pt of it.
        await Assert.That(cell.Width).IsGreaterThan(200f);
        await Assert.That(box.X - cell.X).IsEqualTo((cell.Width - 100) * shareOfSlack).Within(0.01f);
    }
}
