using static EditDocuments;
using static ReviewDocuments;

// A section's page and section breaks, against what Word wrote for the same (Word-probed 2026-10-19,
// see DocumentEditor.Sections.cs). The document is Word's blank one: A4, inch margins all round.
public class SectionTests
{
    const string section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/><w:cols w:space=\"708\"/><w:docGrid w:linePitch=\"360\"/></w:sectPr>";

    static byte[] Document() =>
        Build(P(R("First")) + P(R("Second")) + section);

    static string Sections(byte[] docx) =>
        string.Join(" | ", System.Text.RegularExpressions.Regex.Matches(Xml(docx), "<w:sectPr>.*?</w:sectPr>").Select(_ => _.Value
            .Replace("<w:cols w:space=\"708\" /><w:docGrid w:linePitch=\"360\" />", "")
            .Replace(" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"", "")));

    [Test]
    public async Task ThePage_IsTheSections()
    {
        var setup = DocumentEditor.PageSetupOf(Document(), 0);

        await Assert.That(setup).IsEqualTo(new(11906, 16838, 1440, 1440, 1440, 1440, false));
    }

    [Test]
    public async Task Margins_AreTheSections()
    {
        var edited = DocumentEditor.SetMargins(Document(), 1, 720, 720, 720, 720, Plain);

        await Assert.That(Xml(edited)).Contains("<w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\" />");
    }

    // Moderate margins turned on their side, as Word turns them, and back.
    [Test]
    public async Task Landscape_ExchangesTheSize_AndTurnsTheMargins()
    {
        var moderate = DocumentEditor.SetMargins(Document(), 0, 1440, 1080, 1440, 1080, Plain);

        var landscape = DocumentEditor.SetOrientation(moderate, 0, true, Plain);
        var portrait = DocumentEditor.SetOrientation(landscape, 0, false, Plain);

        await Assert.That(Sections(landscape)).IsEqualTo(
            "<w:sectPr><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\" /><w:pgMar w:top=\"1080\" w:right=\"1440\" w:bottom=\"1080\" w:left=\"1440\" /></w:sectPr>");
        await Assert.That(Sections(portrait)).IsEqualTo(Sections(moderate));
        await Assert.That(DocumentEditor.PageSetupOf(landscape, 0).Landscape).IsTrue();
    }

    [Test]
    public async Task ThePaper_StandsAsThePageDoes()
    {
        var letter = DocumentEditor.SetPaperSize(Document(), 0, 12240, 15840, Plain);
        var landscape = DocumentEditor.SetPaperSize(DocumentEditor.SetOrientation(Document(), 0, true, Plain), 0, 12240, 15840, Plain);

        await Assert.That(Xml(letter)).Contains("<w:pgSz w:w=\"12240\" w:h=\"15840\" />");
        await Assert.That(Xml(landscape)).Contains("<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\" />");
    }

    // At the end of "First", the paragraph ends the section: a copy of the section's properties is its.
    [Test]
    public async Task ASectionBreak_AtAParagraphsEnd_EndsTheSectionThere()
    {
        var result = DocumentEditor.InsertSectionBreak(Document(), 0, 5, false, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[First] ¶ [Second] ¶");
        await Assert.That(Sections(result.Document)).IsEqualTo(
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\" /><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" /></w:sectPr> | " +
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\" /><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" /></w:sectPr>");
        await Assert.That(Xml(result.Document)).StartsWith("<w:p><w:pPr><w:sectPr>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((1, 0));
    }

    // A continuous break is said by the section after it.
    [Test]
    public async Task AContinuousBreak_IsTheNextSectionsType()
    {
        var result = DocumentEditor.InsertSectionBreak(Document(), 0, 5, true, Plain);

        var sections = Sections(result.Document).Split(" | ");
        await Assert.That(sections[0]).DoesNotContain("w:type");
        await Assert.That(sections[1]).StartsWith("<w:sectPr><w:type w:val=\"continuous\" />");
    }

    // In the middle of a paragraph, the paragraph is divided there.
    [Test]
    public async Task ASectionBreak_InAParagraph_DividesIt()
    {
        var result = DocumentEditor.InsertSectionBreak(Document(), 1, 3, false, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[First] ¶ [Sec] ¶ [ond] ¶");
        await Assert.That(Xml(result.Document)).Contains("<w:sectPr>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((2, 0));
    }

    // At the end of the last paragraph, something has to follow for the section after it.
    [Test]
    public async Task ASectionBreak_AtTheEnd_IsFollowedByAnEmptyParagraph()
    {
        var result = DocumentEditor.InsertSectionBreak(Document(), 1, 6, false, Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[First] ¶ [Second] ¶ ¶");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((2, 0));
    }

    // Each section has its own page: turning the second on its side leaves the first as it was.
    [Test]
    public async Task EachSection_HasItsOwnPage()
    {
        var two = DocumentEditor.InsertSectionBreak(Document(), 0, 5, false, Plain).Document;

        var edited = DocumentEditor.SetOrientation(two, 1, true, Plain);

        await Assert.That(DocumentEditor.PageSetupOf(edited, 0).Landscape).IsFalse();
        await Assert.That(DocumentEditor.PageSetupOf(edited, 1).Landscape).IsTrue();
    }

    [Test]
    public async Task ASectionBreak_CannotGoInATable() =>
        await Assert.That(() => DocumentEditor.InsertSectionBreak(
            Build("<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" + P(R("After")) + section),
            0, 2, false, Plain)).Throws<InvalidOperationException>();
}
