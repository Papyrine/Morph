using static EditDocuments;
using static ReviewDocuments;

// Home > Borders and Shading on paragraphs, against what Word wrote (Word-probed 2026-10-23, see
// DocumentEditor.ParagraphBorders.cs).
public class ParagraphBorderTests
{
    static byte[] Document() =>
        Build(P(R("First")) + P(R("Second")) + P(R("Third")));

    static string Line(string side, int space) =>
        $"<w:{side} w:val=\"single\" w:color=\"auto\" w:sz=\"4\" w:space=\"{space}\" />";

    // Word-probed: Outside Borders on two paragraphs, as the ribbon's command writes it.
    [Test]
    public async Task OutsideBorders_AreTheSameBoxOnEachParagraph()
    {
        var edited = DocumentEditor.SetParagraphBorders(Document(), [0, 1], ParagraphBorder.Outside, 4, Plain);

        var box = $"<w:pPr><w:pBdr>{Line("top", 1)}{Line("left", 4)}{Line("bottom", 1)}{Line("right", 4)}</w:pBdr></w:pPr>";
        await Assert.That(Xml(edited)).IsEqualTo(
            $"<w:p>{box}<w:r><w:t xml:space=\"preserve\">First</w:t></w:r></w:p>" +
            $"<w:p>{box}<w:r><w:t xml:space=\"preserve\">Second</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">Third</w:t></w:r></w:p>");
    }

    [Test]
    public async Task ASide_IsAddedToWhatThereIs_AndNoBorderTakesThemAllOff()
    {
        var bottom = DocumentEditor.SetParagraphBorders(Document(), [0], ParagraphBorder.Bottom, 4, Plain);
        var both = DocumentEditor.SetParagraphBorders(bottom, [0], ParagraphBorder.Top, 12, Plain);
        var none = DocumentEditor.SetParagraphBorders(both, [0], ParagraphBorder.None, 4, Plain);

        await Assert.That(Xml(bottom)).Contains($"<w:pBdr>{Line("bottom", 1)}</w:pBdr>");
        await Assert.That(Xml(both)).Contains($"<w:pBdr>{Line("top", 1).Replace("w:sz=\"4\"", "w:sz=\"12\"")}{Line("bottom", 1)}</w:pBdr>");
        await Assert.That(Xml(none)).DoesNotContain("w:pBdr");
    }

    // All Borders is the box and the line between; Inside Horizontal is the line between alone.
    [Test]
    public async Task AllBorders_AddTheLineBetween()
    {
        var all = DocumentEditor.SetParagraphBorders(Document(), [0, 1], ParagraphBorder.All, 4, Plain);
        var inside = DocumentEditor.SetParagraphBorders(Document(), [0, 1], ParagraphBorder.Inside, 4, Plain);

        await Assert.That(Xml(all)).Contains($"{Line("right", 4)}{Line("between", 1)}</w:pBdr>");
        await Assert.That(Xml(inside)).Contains($"<w:pBdr>{Line("between", 1)}</w:pBdr>");
    }

    // Word-probed: shading is a clear pattern with the colour as its fill.
    [Test]
    public async Task Shading_IsTheFill_AndNullTakesItOff()
    {
        var shaded = DocumentEditor.SetParagraphShading(Document(), [1], "FFFF00", Plain);
        var plain = DocumentEditor.SetParagraphShading(shaded, [1], null, Plain);

        await Assert.That(Xml(shaded)).Contains("<w:p><w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFFF00\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Second</w:t>");
        await Assert.That(Xml(plain)).DoesNotContain("w:shd");
    }

    [Test]
    public async Task Borders_Tracked_AreAFormattingChange()
    {
        var edited = DocumentEditor.SetParagraphBorders(Document(), [0], ParagraphBorder.Bottom, 4, Tracked);

        await Assert.That(Xml(edited)).Contains("<w:pPrChange ");
        var rejected = ReviewEditor.ResolveAll(edited, false);
        await Assert.That(Xml(rejected)).DoesNotContain("w:pBdr");
    }
}
