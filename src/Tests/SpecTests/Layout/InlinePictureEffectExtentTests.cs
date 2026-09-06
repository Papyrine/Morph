/// <summary>
/// An inline picture's <c>wp:effectExtent</c>, XPS-read on <c>_probe_picln2</c> (a 150×112.5pt
/// picture at a 72pt margin): the line reserves the extent plus all four effect edges, the picture
/// draws inside that box offset by the left and top edges, and the caption under it moves down by
/// exactly the bottom edge (12, 24 and 30pt) whether or not the picture carries an outline.
/// </summary>
public class InlinePictureEffectExtentTests
{
    static readonly byte[] pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    static readonly PageSettings page = new() { WidthPoints = 400, HeightPoints = 400, MarginTop = 20, MarginBottom = 20, MarginLeft = 30, MarginRight = 30 };

    static ParagraphElement Picture(ImageEffectExtent? effectExtent) =>
        new()
        {
            Runs =
            [
                new()
                {
                    Text = "",
                    Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 },
                    InlineImageData = pixel,
                    InlineImageContentType = "image/png",
                    InlineImageWidthPoints = 100,
                    InlineImageHeightPoints = 60,
                    InlineImageEffectExtent = effectExtent
                }
            ],
            Properties = new()
        };

    [Test]
    public async Task The_line_reserves_the_extent_plus_every_effect_edge()
    {
        var bare = LayoutTestFonts.Measurer.LayoutLineContents(Picture(null), 500)[0];
        var padded = LayoutTestFonts.Measurer.LayoutLineContents(Picture(new(10, 20, 30, 40)), 500)[0];

        await Assert.That(bare.Height).IsEqualTo(60f).Within(0.01f);
        await Assert.That(padded.Height).IsEqualTo(60f + 20 + 40).Within(0.01f);
        await Assert.That(padded.Images[0].BoxWidth).IsEqualTo(100f + 10 + 30).Within(0.01f);
        await Assert.That(padded.Images[0].Width).IsEqualTo(100f).Within(0.01f);
    }

    [Test]
    public async Task The_picture_draws_inside_the_box_offset_by_the_left_and_top_edges()
    {
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer).Layout([Picture(new(10, 20, 30, 40))], page);
        var line = laidOut.Pages[0].Items.OfType<PlacedLine>().Single();
        var image = line.Images.Single();

        // The box sits on the line's bottom (an image-only line puts its baseline there), the
        // picture 10pt in from the left and 40pt up from the baseline — 20pt below the line top.
        await Assert.That(image.X).IsEqualTo((float) page.MarginLeft + 10).Within(0.01f);
        await Assert.That(image.Y).IsEqualTo(line.Baseline - 40 - 60).Within(0.01f);
        await Assert.That(image.Y).IsEqualTo(line.Y + 20).Within(0.01f);
        await Assert.That(image.Width).IsEqualTo(100f).Within(0.01f);
        await Assert.That(image.Height).IsEqualTo(60f).Within(0.01f);
    }

    [Test]
    public async Task A_following_paragraph_starts_below_the_whole_box()
    {
        var caption = new ParagraphElement { Runs = [new() { Text = "cap", Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 } }], Properties = new() };
        var bare = new Fragmenter(LayoutTestFonts.Measurer).Layout([Picture(null), caption], page);
        var padded = new Fragmenter(LayoutTestFonts.Measurer).Layout([Picture(new(0, 0, 0, 30)), caption], page);

        var bareCaption = bare.Pages[0].Items.OfType<PlacedLine>().Last();
        var paddedCaption = padded.Pages[0].Items.OfType<PlacedLine>().Last();
        await Assert.That(paddedCaption.Baseline - bareCaption.Baseline).IsEqualTo(30f).Within(0.01f);
    }
}
