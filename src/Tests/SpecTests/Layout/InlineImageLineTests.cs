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

    static Run Text(string text) => new()
    {
        Text = text,
        Properties = new() { FontFamily = "Aptos", FontSizePoints = 12 }
    };

    [Test]
    public async Task A_small_image_alone_sits_on_the_bottom_of_the_mark_line()
    {
        var textOnly = new ParagraphElement { Runs = [Text("x")], Properties = new() };
        var imageOnly = new ParagraphElement { Runs = [Image(4)], Properties = new() };

        var textLine = LayoutTestFonts.Measurer.LayoutLineContents(textOnly, 500)[0];
        var imageLine = LayoutTestFonts.Measurer.LayoutLineContents(imageOnly, 500)[0];

        await Assert.That(imageLine.Height).IsEqualTo(textLine.Height).Within(0.01f);
        await Assert.That(imageLine.Ascent).IsEqualTo(imageLine.Height).Within(0.01f);
        await Assert.That(textLine.Ascent).IsLessThan(textLine.Height - 1f);
    }

    [Test]
    public async Task A_tall_image_alone_makes_a_line_of_its_own_height_with_no_descent()
    {
        var imageOnly = new ParagraphElement { Runs = [Image(40)], Properties = new() };

        var line = LayoutTestFonts.Measurer.LayoutLineContents(imageOnly, 500)[0];

        await Assert.That(line.Height).IsEqualTo(40f).Within(0.01f);
        await Assert.That(line.Ascent).IsEqualTo(40f).Within(0.01f);
    }

    [Test]
    public async Task An_image_on_a_text_line_keeps_the_text_descent_under_the_baseline()
    {
        var textOnly = new ParagraphElement { Runs = [Text("x")], Properties = new() };
        var mixed = new ParagraphElement { Runs = [Text("x "), Image(40)], Properties = new() };

        var textLine = LayoutTestFonts.Measurer.LayoutLineContents(textOnly, 500)[0];
        var line = LayoutTestFonts.Measurer.LayoutLineContents(mixed, 500)[0];
        var descent = textLine.Height - textLine.Ascent;

        await Assert.That(line.Ascent).IsEqualTo(40f).Within(0.01f);
        await Assert.That(line.Height).IsEqualTo(40f + descent).Within(0.01f);
    }
}
