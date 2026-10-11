/// <summary>
/// Where an inline image sits in its line, XPS-read on <c>_probe_inline</c>, <c>_probe_inline2</c> and
/// <c>_probe_r12</c>: on a text line its bottom is on the baseline and the line keeps the text descent
/// under it; alone in its paragraph the line is max(mark pitch, image) and the image sits on the line
/// BOTTOM, the baseline with it.
/// </summary>
public class InlineImageLineTests
{
    static readonly byte[] pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    static Run Image(double points) => new()
    {
        Text = "",
        Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 },
        InlineImageData = pixel,
        InlineImageContentType = "image/png",
        InlineImageWidthPoints = points,
        InlineImageHeightPoints = points
    };

    // The probes behind the first three tests were single-spaced; the multiple has its own below.
    static ParagraphProperties Single => new() { LineSpacingMultiplier = 1 };

    static Run Text(string text) => new()
    {
        Text = text,
        Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 }
    };

    [Test]
    public async Task A_small_image_alone_sits_on_the_bottom_of_the_mark_line()
    {
        var textOnly = new ParagraphElement { Runs = [Text("x")], Properties = Single };
        var imageOnly = new ParagraphElement { Runs = [Image(4)], Properties = Single };

        var textLine = LayoutTestFonts.Measurer.LayoutLineContents(textOnly, 500)[0];
        var imageLine = LayoutTestFonts.Measurer.LayoutLineContents(imageOnly, 500)[0];

        await Assert.That(imageLine.Height).IsEqualTo(textLine.Height).Within(0.01f);
        await Assert.That(imageLine.Ascent).IsEqualTo(imageLine.Height).Within(0.01f);
        await Assert.That(textLine.Ascent).IsLessThan(textLine.Height - 1f);
    }

    [Test]
    public async Task A_tall_image_alone_makes_a_line_of_its_own_height_with_no_descent()
    {
        var imageOnly = new ParagraphElement { Runs = [Image(40)], Properties = Single };

        var line = LayoutTestFonts.Measurer.LayoutLineContents(imageOnly, 500)[0];

        await Assert.That(line.Height).IsEqualTo(40f).Within(0.01f);
        await Assert.That(line.Ascent).IsEqualTo(40f).Within(0.01f);
    }

    // _probe_picline: a 72pt picture alone in its paragraph. Under a 10pt Arial mark (11.5pt pitch) the
    // line is 71.9 single, 77.9 at 1.5 and 83.9 double; under a 30pt mark (34.5) 71.9, 89.3, 106.7, and
    // 65.3 at 0.8. The picture stands at the line's top with the leading under it, and through the top
    // of a line the multiple made shorter than it.
    [Test]
    [Arguments(10, 1.5, 77.9)]
    [Arguments(10, 2.0, 83.9)]
    [Arguments(30, 1.5, 89.3)]
    [Arguments(30, 2.0, 106.7)]
    [Arguments(30, 0.8, 65.3)]
    public async Task A_multiple_adds_its_share_of_the_mark_pitch_to_a_picture_line(double markPoints, double multiple, double wordHeight)
    {
        var picture = Image(72);
        var paragraph = new ParagraphElement
        {
            Runs = [new() { Text = "", Properties = new() { FontFamily = "Arial", FontSizePoints = markPoints }, InlineImageData = picture.InlineImageData, InlineImageContentType = picture.InlineImageContentType, InlineImageWidthPoints = 72, InlineImageHeightPoints = 72 }],
            Properties = new()
            {
                LineSpacingMultiplier = multiple,
                ParagraphMarkRunProperties = new() { FontFamily = "Arial", FontSizePoints = markPoints }
            }
        };

        var line = LayoutTestFonts.Measurer.LayoutLineContents(paragraph, 500)[0];
        var measured = LayoutTestFonts.Measurer.LayoutLines(paragraph, 500)[0];

        await Assert.That((double) line.Height).IsEqualTo(wordHeight).Within(0.5);
        await Assert.That(measured.Height).IsEqualTo(line.Height).Within(0.01f);

        // The picture's bottom is the baseline: 72pt down a line with leading under it, the line's
        // own bottom where the multiple took height away.
        await Assert.That(line.Ascent).IsEqualTo(Math.Min(72f, line.Height)).Within(0.01f);
    }

    [Test]
    public async Task A_connector_with_no_height_adds_none_to_its_line()
    {
        // resumes/11's divider rules: an inline straight connector, 540pt wide and 0 tall, alone in a
        // paragraph. The line is the mark's own, and the rule stands on its baseline.
        var group = new InlineShapeGroup { ChildExtentX = 100, ChildExtentY = 100, Shapes = [] };
        var rule = new Run { Text = "", Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 }, InlineShapeGroup = group, InlineImageWidthPoints = 540, InlineImageHeightPoints = 0 };
        var mark = Single with { ParagraphMarkRunProperties = new() { FontFamily = "Aptos", FontSizePoints = 12 } };
        var ruled = new ParagraphElement { Runs = [rule], Properties = mark };
        var empty = new ParagraphElement { Runs = [], Properties = mark };

        var line = LayoutTestFonts.Measurer.LayoutLineContents(ruled, 600)[0];
        var markLine = LayoutTestFonts.Measurer.LayoutLineContents(empty, 600)[0];

        await Assert.That(line.Height).IsEqualTo(markLine.Height).Within(0.01f);
        await Assert.That(line.Ascent).IsEqualTo(markLine.Ascent).Within(0.01f);
        await Assert.That(line.Images[0].BoxHeight).IsEqualTo(0f);
    }

    [Test]
    public async Task An_image_on_a_text_line_keeps_the_text_descent_under_the_baseline()
    {
        var textOnly = new ParagraphElement { Runs = [Text("x")], Properties = Single };
        var mixed = new ParagraphElement { Runs = [Text("x "), Image(40)], Properties = Single };

        var textLine = LayoutTestFonts.Measurer.LayoutLineContents(textOnly, 500)[0];
        var line = LayoutTestFonts.Measurer.LayoutLineContents(mixed, 500)[0];
        var descent = textLine.Height - textLine.Ascent;

        await Assert.That(line.Ascent).IsEqualTo(40f).Within(0.01f);
        await Assert.That(line.Height).IsEqualTo(40f + descent).Within(0.01f);
    }
}
