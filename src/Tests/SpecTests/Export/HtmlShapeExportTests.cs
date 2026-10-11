/// <summary>
/// Tests for how the HTML export draws a floating shape: a line connector, a dashed outline, an
/// outline that reaches past its box, and a picture fill cut to the shape.
/// </summary>
public class HtmlShapeExportTests
{
    static string Export(params DocumentElement[] elements) =>
        HtmlExporter.Export(Doc(elements), new() {EmitDocument = false});

    // Four bytes stand in for a picture: the exporter embeds what it is given without decoding it.
    static readonly byte[] picture = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>
    /// A shape with no height is a line connector. An <c>&lt;svg&gt;</c> with a zero height
    /// renders nothing, so the line is given a viewport as thick as its stroke, centred on where
    /// it runs. The fold, cut and tear guides of cards/12, cards/18 and labels/03 are these, and
    /// each was dropped whole.
    /// </summary>
    [Test]
    public async Task HorizontalLineShape_IsDrawnInAViewportAsThickAsItsStroke()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 200,
            HeightPoints = 0,
            HorizontalPositionPoints = 10,
            VerticalPositionPoints = 40,
            LineColorHex = "00A88E",
            LineWidthPoints = 6
        });

        await Assert.That(html).Contains("left: 10pt; top: 37pt; width: 200pt; height: 6pt");
        await Assert.That(html).Contains("""<line x1="0" y1="3" x2="200" y2="3" stroke="#00a88e" stroke-width="6" />""");
    }

    [Test]
    public async Task VerticalLineShape_RunsDownItsViewport()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 0,
            HeightPoints = 248,
            HorizontalPositionPoints = 100,
            LineColorHex = "D9D9D9",
            LineWidthPoints = 4
        });

        await Assert.That(html).Contains("left: 98pt; top: 0pt; width: 4pt; height: 248pt");
        await Assert.That(html).Contains("""<line x1="2" y1="0" x2="2" y2="248" stroke="#d9d9d9" stroke-width="4" />""");
    }

    /// <summary>A line with nothing to stroke it, or with no extent on either axis, draws nothing.</summary>
    [Test]
    public async Task LineShape_WithoutAStrokeOrALength_IsNotWritten()
    {
        var unstroked = Export(new FloatingShapeElement {WidthPoints = 200, HeightPoints = 0, FillColorHex = "FF0000"});
        var point = Export(new FloatingShapeElement {WidthPoints = 0, HeightPoints = 0, LineColorHex = "000000", LineWidthPoints = 2});

        await Assert.That(unstroked).DoesNotContain("<svg");
        await Assert.That(point).DoesNotContain("<svg");
    }

    /// <summary>
    /// DrawingML states a preset dash in multiples of the line's width; SVG takes lengths. labels/03's
    /// tear lines are 1.75pt <c>sysDot</c> connectors: one width on, one off.
    /// </summary>
    [Test]
    public async Task DashPattern_IsScaledByTheLineWidth()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 198,
            HeightPoints = 0,
            LineColorHex = "000000",
            LineWidthPoints = 2,
            LineDashPattern = [1, 3]
        });

        await Assert.That(html).Contains("stroke-dasharray=\"2 6\"");
    }

    /// <summary>
    /// The outline's width follows the painters (<c>ShapeStroke</c>): none declared is DrawingML's
    /// 0.75pt default and a declared zero is a hairline. Requiring a positive width drew neither.
    /// </summary>
    [Test]
    public async Task Outline_WithNoWidthOrAZeroWidth_IsStillStroked()
    {
        var defaulted = Export(new FloatingShapeElement {WidthPoints = 50, HeightPoints = 50, LineColorHex = "112233"});
        var hairline = Export(new FloatingShapeElement {WidthPoints = 50, HeightPoints = 50, LineColorHex = "112233", LineWidthPoints = 0});

        await Assert.That(defaulted).Contains("stroke=\"#112233\" stroke-width=\"0.75\"");
        await Assert.That(hairline).Contains("stroke=\"#112233\" stroke-width=\"0.5\"");
    }

    /// <summary>
    /// Nothing clips a shape to its own box: an outline is stroked centred on the geometry and a
    /// rotated shape turns out of the box it was declared in. agendas-minutes/08's 15pt ring lost
    /// its outer half to the viewport.
    /// </summary>
    [Test]
    public async Task ShapeViewport_DoesNotClipWhatReachesPastTheBox()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 80,
            HeightPoints = 80,
            Preset = PresetShape.Ellipse,
            FillColorHex = "F4B894",
            LineColorHex = "D55D15",
            LineWidthPoints = 15
        });

        await Assert.That(html).Contains("overflow: visible");
    }

    /// <summary>
    /// An unwarped WordArt's box is drawn as its chrome, and an ellipse box is the oval inscribed
    /// in it. wedding/08's "&amp;" badge is a circle and came out as the square around it.
    /// </summary>
    [Test]
    public async Task EllipseWordArtBox_IsRound()
    {
        var html = Export(new WordArtElement
        {
            Text = "&",
            WidthPoints = 76.5,
            HeightPoints = 76.5,
            BoxFillColorHex = "4A9B83",
            BoxIsEllipse = true
        });
        var square = Export(new WordArtElement
        {
            Text = "&",
            WidthPoints = 76.5,
            HeightPoints = 76.5,
            BoxFillColorHex = "4A9B83"
        });

        await Assert.That(html).Contains("background-color: #4a9b83; border-radius: 50%; width: 76.5pt; min-height: 76.5pt");
        await Assert.That(square).DoesNotContain("border-radius");
    }

    /// <summary>
    /// A picture fill wins over a fill colour the shape also resolved, as it does in the layout
    /// engine, and is cut to the shape's own outline. newsletters/13's arch drew as a block of the
    /// colour with no photo in it.
    /// </summary>
    [Test]
    public async Task PictureFill_WinsOverAFillColour_AndIsCutToTheShape()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 220,
            HeightPoints = 226,
            Preset = PresetShape.Ellipse,
            FillColorHex = "484138",
            ImageData = picture,
            ImageContentType = "image/png",
            LineColorHex = "E7E0D2",
            LineWidthPoints = 1
        });

        await Assert.That(html).Contains("""<clipPath id="shape-clip-0"><ellipse cx="110" cy="113" rx="110" ry="113" /></clipPath>""");
        await Assert.That(html).Contains("""<g clip-path="url(#shape-clip-0)"><image href="data:image/png;base64,""");
        await Assert.That(html).DoesNotContain("#484138");
        // The outline is stroked over the picture, unfilled.
        await Assert.That(html).Contains("""<ellipse cx="110" cy="113" rx="110" ry="113" fill="none" stroke="#e7e0d2" stroke-width="1" />""");
    }

    /// <summary>
    /// A cropped fill is drawn at the enlarged rectangle its crop implies and clipped back to the
    /// box, since SVG's <c>&lt;image&gt;</c> has no source rectangle.
    /// </summary>
    [Test]
    public async Task PictureFill_Cropped_IsEnlargedAndClippedToTheBox()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 100,
            HeightPoints = 50,
            ImageData = picture,
            ImageContentType = "image/png",
            ImageCrop = new() {Left = 0.5}
        });

        await Assert.That(html).Contains("""<clipPath id="shape-clip-0"><rect x="0" y="0" width="100" height="50" /></clipPath>""");
        await Assert.That(html).Contains("x=\"-100\" y=\"0\" width=\"200\" height=\"50\"");
    }

    /// <summary>A plain picture fill, neither cut nor cropped, needs no clip.</summary>
    [Test]
    public async Task PictureFill_Plain_IsStretchedOverTheBoxWithoutAClip()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 100,
            HeightPoints = 50,
            ImageData = picture,
            ImageContentType = "image/png"
        });

        await Assert.That(html).DoesNotContain("clipPath");
        await Assert.That(html).Contains("""x="0" y="0" width="100" height="50" preserveAspectRatio="none" />""");
    }

    /// <summary>
    /// A tiled fill (<c>a:tile</c>) repeats the picture at its own size from the corner it is
    /// aligned to, as an SVG pattern painted into the shape's outline.
    /// </summary>
    [Test]
    public async Task PictureFill_Tiled_IsAPatternAtTheTilesOwnSize()
    {
        var html = Export(new FloatingShapeElement
        {
            WidthPoints = 100,
            HeightPoints = 60,
            ImageData = picture,
            ImageContentType = "image/png",
            ImageTile = new(20, 10, 5, 0, ImageTileAlignment.TopLeft)
        });

        await Assert.That(html).Contains("""<pattern id="shape-tile-0" patternUnits="userSpaceOnUse" x="5" y="0" width="20" height="10">""");
        await Assert.That(html).Contains("""<rect x="0" y="0" width="100" height="60" fill="url(#shape-tile-0)" />""");
    }
}
