/// <summary>
/// Character spacing (w:spacing in rPr) widens text during layout, causing earlier line wrapping
/// compared to the same text without spacing. Asserted against the canonical measurer — the one
/// layout model since the production renderers were deleted.
/// </summary>
public class CharacterSpacingTests
{
    static float TotalHeight(ParagraphElement paragraph, float width) =>
        LayoutTestFonts.Measurer.LayoutParagraphForMeasurement(paragraph, width).Sum();

    static ParagraphElement Paragraph(string text, double characterSpacing = 0, double fontSize = 11) =>
        new()
        {
            Runs = [new() {Text = text, Properties = new() {FontSizePoints = fontSize, CharacterSpacingPoints = characterSpacing}}],
            Properties = new()
        };

    [Test]
    public async Task CharacterSpacing_CausesEarlierWrapping()
    {
        // Text carefully sized: fits on one line at 120pt without spacing,
        // but wraps with 3pt character spacing (~20 chars * 3pt = 60pt extra)
        var text = "Sample text for test";
        const float cellWidth = 120;

        var heightNoSpacing = TotalHeight(Paragraph(text), cellWidth);
        var heightWithSpacing = TotalHeight(Paragraph(text, characterSpacing: 3.0), cellWidth);

        // Character spacing makes text wider → wraps to more lines → taller
        await Assert.That(heightWithSpacing).IsGreaterThan(heightNoSpacing);
    }

    [Test]
    public async Task CharacterSpacing_Zero_SameAsDefault()
    {
        var text = "Hello World";

        var height1 = TotalHeight(Paragraph(text, fontSize: 14), 260);
        var height2 = TotalHeight(Paragraph(text, characterSpacing: 0, fontSize: 14), 260);

        await Assert.That(height1).IsEqualTo(height2).Within(0.01f);
    }

    [Test]
    public async Task CharacterSpacing_AffectsTableCellLayout()
    {
        // The same widening at a table-cell width: spacing pushes the text onto more lines.
        var text = "This text should wrap differently with character spacing applied";
        const float cellWidth = 150;

        var heightNoSpacing = TotalHeight(Paragraph(text), cellWidth);
        var heightWithSpacing = TotalHeight(Paragraph(text, characterSpacing: 1.5), cellWidth);

        await Assert.That(heightWithSpacing).IsGreaterThan(heightNoSpacing);
    }

    [Test]
    public async Task CharacterSpacing_ParsedFromDocx()
    {
        // The wedding/01 document has Subtitle style with w:spacing w:val="20" (1pt).
        // Verify the parser extracts it correctly.
        var parser = new DocumentParser();
        await using var stream = File.OpenRead(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "wedding", "01", "input.docx"));
        var doc = parser.Parse(stream);

        // Find a paragraph with the Subtitle style (has character spacing from style rPr)
        var subtitleRun = doc.Elements
            .OfType<TableElement>()
            .SelectMany(_ => _.Rows)
            .SelectMany(_ => _.Cells)
            .SelectMany(_ => _.Content)
            .OfType<ParagraphElement>()
            .Where(_ => _.Properties.StyleId == "Subtitle")
            .SelectMany(_ => _.Runs)
            .First();

        // Subtitle style has w:spacing w:val="20" → 20 twips → 1pt
        await Assert.That(subtitleRun.Properties.CharacterSpacingPoints).IsEqualTo(1.0);
    }
}
