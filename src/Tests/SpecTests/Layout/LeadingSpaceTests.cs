/// <summary>
/// Spaces before a line's first word stay on the line, and push the word along, where nothing was
/// wrapped: at a paragraph's start and after a line break. Only a wrap leaves them behind, hanging at
/// the end of the line above. Word-probed (_probe_lead, 2026-11-28, 24pt Aptos): four leading spaces
/// stand the text 40px in at 150 DPI at a paragraph's start and after a <c>w:br</c> alike, eight move a
/// centred line 40px, and the spaces at a soft wrap leave the next line at the margin. Excel prints a
/// cell's the same way: modern-basic-purple-timesheet-invoice's title is <c>" INVOICE"</c>.
/// </summary>
public class LeadingSpaceTests
{
    static readonly CanonicalParagraphMeasurer measurer = LayoutTestFonts.Measurer;

    static readonly RunProperties properties = new() {FontFamily = "Arial", FontSizePoints = 24};

    static ParagraphElement Paragraph(string text) =>
        new()
        {
            Runs = [new() {Text = text, Properties = properties}]
        };

    static float Width(string text) =>
        measurer.LayoutLineContents(Paragraph(text), 400)[0].Width;

    // One space's width at the run's size, as the difference it makes between two words
    static float Space => Width("X X") - Width("XX");

    [Test]
    public async Task Leading_spaces_at_a_paragraph_start_stay_on_its_line()
    {
        await Assert.That(Space).IsGreaterThan(5f);
        await Assert.That(Width("    XXXX")).IsEqualTo(Width("XXXX") + 4 * Space).Within(1f);
    }

    [Test]
    public async Task Leading_spaces_after_a_line_break_stay_on_its_line()
    {
        var lines = measurer.LayoutLineContents(Paragraph("XX\n    XXXX"), 400);

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[1].Width).IsEqualTo(Width("XXXX") + 4 * Space).Within(1f);
    }

    [Test]
    public async Task Spaces_at_a_wrap_hang_on_the_line_above()
    {
        // Twenty capitals fill 320pt of the 400pt measure, too little for the four spaces and the word after
        var lines = measurer.LayoutLineContents(Paragraph(new string('X', 20) + "    XXXX"), 400);

        await Assert.That(lines.Count).IsEqualTo(2);
        await Assert.That(lines[1].Width).IsEqualTo(Width("XXXX")).Within(0.01f);
    }
}
