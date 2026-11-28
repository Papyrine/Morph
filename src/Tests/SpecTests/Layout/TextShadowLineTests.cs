/// <summary>
/// A run's w14:shadow takes room in its line: Word deepens the line by the shadow's vertical offset,
/// under the text for a shadow cast down and over it for one cast up (_probe_m07s: menus/07's title with
/// its shadow 12pt straight down, straight up and at 44 degrees, read off Word's pages).
/// </summary>
public class TextShadowLineTests
{
    static ParagraphElement Paragraph(TextShadow? shadow) =>
        new()
        {
            Runs = [new() { Text = "Menu", Properties = new() { FontFamily = "Arial", FontSizePoints = 24, Shadow = shadow } }],
            Properties = new() { SpacingAfterPoints = 0 }
        };

    static TextShadow Shadow(double direction) =>
        new() { ColorHex = "000000", DistancePoints = 12, DirectionDegrees = direction, BlurPoints = 0, AlphaPercent = 100 };

    static float Height(TextShadow? shadow) =>
        LayoutTestFonts.Measurer.LayoutParagraphForMeasurement(Paragraph(shadow), 400).Sum();

    [Test]
    [Arguments(90, 12)]
    [Arguments(270, 12)]
    [Arguments(44, 8.336)]
    [Arguments(0, 0)]
    public async Task A_shadow_deepens_its_line_by_its_vertical_offset(double direction, double reach)
    {
        // The line's pitch lands on the layout's pixel grid, so the reach comes back rounded up to it.
        var grown = (double) (Height(Shadow(direction)) - Height(null));
        await Assert.That(grown).IsGreaterThanOrEqualTo(reach - 0.01);
        await Assert.That(grown).IsLessThan(reach + 1);
    }

    // An empty paragraph's mark takes its shadow's room too (menus/09's empty Title paragraphs beside
    // its "Menu", which set the row's height).
    [Test]
    public async Task A_shadowed_mark_deepens_its_empty_line()
    {
        static float Empty(TextShadow? shadow) =>
            LayoutTestFonts.Measurer.LayoutParagraphForMeasurement(
                new() { Runs = [], Properties = new() { ParagraphMarkFontSizePoints = 24, ParagraphMarkFontFamily = "Arial", ParagraphMarkShadow = shadow } },
                400).Sum();

        var grown = (double) (Empty(Shadow(90)) - Empty(null));
        await Assert.That(grown).IsGreaterThanOrEqualTo(12 - 0.01);
        await Assert.That(grown).IsLessThan(13);
    }

    // Cast up, the room goes over the text: the baseline drops by the shadow's reach.
    [Test]
    public async Task A_shadow_cast_up_lowers_the_baseline()
    {
        var plain = LayoutBaseline(null);
        var raised = LayoutBaseline(Shadow(270));

        await Assert.That((double) (raised - plain)).IsEqualTo(12).Within(0.01);
        await Assert.That((double) (LayoutBaseline(Shadow(90)) - plain)).IsEqualTo(0).Within(0.01);

        static float LayoutBaseline(TextShadow? shadow)
        {
            var laidOut = new Fragmenter(LayoutTestFonts.Measurer)
                .Layout([Paragraph(shadow)], new() { WidthPoints = 400, HeightPoints = 400, MarginTop = 20, MarginBottom = 20, MarginLeft = 20, MarginRight = 20 });
            return laidOut.Pages[0].Items.OfType<PlacedLine>().First().Baseline;
        }
    }
}
