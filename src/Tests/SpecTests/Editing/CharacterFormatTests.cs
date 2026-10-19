using DocumentFormat.OpenXml.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// The look of text — its font, size, colour, highlight and position — set on what lies between two
// places, and taken off again. Word-probed 2026-10-18 for the markup each is written as.
public class CharacterFormatTests
{
    static SourcePosition At(int run, int offset) =>
        new(run, offset);

    static byte[] Format(byte[] docx, RunFormat format, EditOptions? options = null) =>
        DocumentEditor.Format(docx, At(0, 0), At(0, 4), format, options ?? Plain);

    [Test]
    public async Task AFont_IsWrittenForEveryScriptButEastAsian()
    {
        var edited = Format(Build(P(R("Font here"))), new(Font: "Arial"));

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\" /></w:rPr><w:t xml:space=\"preserve\">Font</w:t></w:r>");
    }

    // A theme font named on the run would win over the face set: it goes.
    [Test]
    public async Task AFont_TakesThePlaceOfTheThemes()
    {
        var docx = Build(P("<w:r><w:rPr><w:rFonts w:asciiTheme=\"minorHAnsi\" w:hAnsiTheme=\"minorHAnsi\" w:eastAsiaTheme=\"minorEastAsia\"/></w:rPr><w:t>Font here</w:t></w:r>"));

        var edited = Format(docx, new(Font: "Georgia"));

        await Assert.That(Xml(edited)).Contains("<w:rFonts w:ascii=\"Georgia\" w:hAnsi=\"Georgia\" w:cs=\"Georgia\" w:eastAsiaTheme=\"minorEastAsia\" />");
    }

    [Test]
    public async Task ASize_IsInHalfPoints()
    {
        var edited = Format(Build(P(R("Size here"))), new(SizePoints: 18));

        await Assert.That(Xml(edited)).StartsWith("<w:p><w:r><w:rPr><w:sz w:val=\"36\" /></w:rPr>");
    }

    [Test]
    public async Task AColour_IsWithoutATheme_AndAutomaticTakesItAway()
    {
        var docx = Build(P("<w:r><w:rPr><w:color w:val=\"0F4761\" w:themeColor=\"accent1\"/></w:rPr><w:t>Colour here</w:t></w:r>"));

        var red = Format(docx, new(Color: "ff0000"));
        var automatic = Format(red, new(Color: "auto"));

        await Assert.That(Xml(red)).StartsWith("<w:p><w:r><w:rPr><w:color w:val=\"FF0000\" /></w:rPr>");
        await Assert.That(Xml(automatic)).StartsWith("<w:p><w:r><w:rPr /><w:t");
    }

    [Test]
    public async Task AHighlight_IsWordsColour_AndNoneTakesItAway()
    {
        var yellow = Format(Build(P(R("Highlight here"))), new(Highlight: "yellow"));
        var none = Format(yellow, new(Highlight: "none"));

        await Assert.That(Xml(yellow)).StartsWith("<w:p><w:r><w:rPr><w:highlight w:val=\"yellow\" /></w:rPr>");
        await Assert.That(Xml(none)).DoesNotContain("highlight");
    }

    [Test]
    public async Task SuperscriptAndSubscript_AreTheVerticalAlignment_AndTheBaselineTakesItAway()
    {
        var super = Format(Build(P(R("Super here"))), new(Vertical: VerticalPositionValues.Superscript));
        var sub = Format(super, new(Vertical: VerticalPositionValues.Subscript));
        var baseline = Format(sub, new(Vertical: VerticalPositionValues.Baseline));

        await Assert.That(Xml(super)).Contains("<w:vertAlign w:val=\"superscript\" />");
        await Assert.That(Xml(sub)).Contains("<w:vertAlign w:val=\"subscript\" />");
        await Assert.That(Xml(baseline)).DoesNotContain("vertAlign");
    }

    [Test]
    public async Task ALook_Tracked_IsAFormattingChange_ThatRejectingTakesAway()
    {
        var docx = Build(P(R("Word here")));

        var edited = Format(docx, new(Font: "Arial", SizePoints: 20, Color: "C00000"), Tracked);

        await Assert.That(Shape(edited)).IsEqualTo("[~:Word][ here] ¶");
        await Assert.That(DocumentReview.Read(edited).Changes.Select(_ => _.Kind)).IsEquivalentTo([ReviewChangeKind.Formatting]);
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo("[Word][ here] ¶");
    }

    // Word-probed 2026-10-18: Clear Formatting on a word made bold, 20pt and red leaves no run
    // properties. A character style is the text's kind rather than its look, and stays.
    [Test]
    public async Task ClearFormatting_TakesTheLookOff_AndLeavesTheStyle()
    {
        var docx = Build(P(
            "<w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/><w:b/><w:sz w:val=\"40\"/><w:color w:val=\"FF0000\"/></w:rPr><w:t>Link</w:t></w:r>",
            R(" plain", bold: true)));

        var edited = DocumentEditor.ClearFormatting(docx, At(0, 0), At(1, 6), Plain);

        await Assert.That(Xml(edited)).IsEqualTo(
            "<w:p><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\" /></w:rPr><w:t>Link</w:t></w:r><w:r><w:t xml:space=\"preserve\"> plain</w:t></w:r></w:p>");
    }

    [Test]
    public async Task ClearFormatting_Tracked_KeepsARecordOfTheLook()
    {
        var docx = Build(P(R("Bold", bold: true)));

        var edited = DocumentEditor.ClearFormatting(docx, At(0, 0), At(0, 4), Tracked);

        await Assert.That(Shape(edited)).IsEqualTo("[~:Bold] ¶");
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo("[b:Bold] ¶");
    }
}
