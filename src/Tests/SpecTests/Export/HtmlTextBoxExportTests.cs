/// <summary>
/// Tests for a text box the text does not wrap round. It floats over the page like a wrap-NONE
/// picture, so the HTML export places it the same way — absolutely, at its own size, out of the
/// flow — where it used to be written into the flow and take the room of its text.
/// </summary>
public class HtmlTextBoxExportTests
{
    static string Export(params DocumentElement[] elements) =>
        HtmlExporter.Export(Doc(elements), new() {EmitDocument = false});

    static FloatingTextBoxElement TextBox(string text, WrapType wrap = WrapType.None, bool hidesOverflow = true, double rotation = 0, string? fill = null) =>
        new()
        {
            Content = [Para(TextRun(text))],
            WidthPoints = 120,
            HeightPoints = 40,
            HorizontalPositionPoints = 30,
            VerticalPositionPoints = 200,
            WrapType = wrap,
            HidesOverflow = hidesOverflow,
            RotationDegrees = rotation,
            BackgroundColorHex = fill,
            BehindText = true
        };

    /// <summary>
    /// labels/06's twenty ADMIT ONE stubs are these: written into the flow they stacked as a column
    /// at the head of the sheet and pushed everything under them down against the art.
    /// </summary>
    [Test]
    public async Task WrapNoneTextBox_IsPlacedAbsolutelyAtItsOwnSize()
    {
        var html = Export(TextBox("ADMIT ONE"), Para(TextRun("flow")));

        await Assert.That(html).Contains("<div style=\"position: absolute; left: 30pt; top: 200pt; width: 120pt; height: 40pt; overflow: hidden; z-index: -1\">\n      <p>ADMIT ONE</p>");
        // The box takes no room in the flow, so the page is held open as far as it reaches instead.
        await Assert.That(html).StartsWith("<div style=\"min-height: 240pt\">");
    }

    /// <summary>A box that grows with its text is given its height as a minimum, not a clip.</summary>
    [Test]
    public async Task TextBoxThatDoesNotHideItsOverflow_KeepsItsHeightAsAMinimum()
    {
        var html = Export(TextBox("grows", hidesOverflow: false));

        await Assert.That(html).Contains("width: 120pt; min-height: 40pt; z-index: -1");
        await Assert.That(html).DoesNotContain("overflow: hidden");
    }

    /// <summary>A rotated box turns about its centre with its text, as the layout engine turns it.</summary>
    [Test]
    public async Task RotatedTextBox_TurnsWithItsText()
    {
        var html = Export(TextBox("ADMIT ONE", rotation: 270));

        await Assert.That(html).Contains("overflow: hidden; transform: rotate(270deg); z-index: -1");
    }

    /// <summary>
    /// The box's fill is a shape of the box's size behind the text, so an empty box still draws
    /// its fill (cards/13's page-sized wash holds one dash and drew as a strip a line tall) and
    /// writes no empty text block.
    /// </summary>
    [Test]
    public async Task TextBoxFill_IsAShapeBehindTheText_AndAnEmptyBoxIsOnlyThat()
    {
        var filled = Export(TextBox("text", fill: "C0E3EC"));
        var empty = Export(new FloatingTextBoxElement
        {
            Content = [Para()],
            WidthPoints = 120,
            HeightPoints = 40,
            BackgroundColorHex = "C0E3EC"
        });

        await Assert.That(filled).Contains("<rect x=\"0\" y=\"0\" width=\"120\" height=\"40\" fill=\"#c0e3ec\" />");
        await Assert.That(filled).Contains("<p>text</p>");
        await Assert.That(empty).Contains("fill=\"#c0e3ec\"");
        await Assert.That(empty).DoesNotContain("<p");
    }

    /// <summary>A text box the text wraps round keeps its place in the flow.</summary>
    [Test]
    public async Task WrappedTextBox_StaysInTheFlow()
    {
        var html = Export(TextBox("wrapped", WrapType.Square));

        await Assert.That(html).IsEqualTo("<div>\n  <p>wrapped</p>\n</div>\n");
    }
}
