using static EditDocuments;
using static ReviewDocuments;

// What Word's keys do in a list and after a heading, as far as the file goes: what the outline tells
// the editor about a paragraph, and Backspace at a list paragraph's start. Word-probed 2026-10-19 by
// keys sent to Word 365: Tab in a list's paragraph moves it a level in and Shift+Tab out (that is
// DocumentEditor.Indent), Enter on an empty one takes it out of the list (SetList with None), and
// Backspace at its start takes its number off and leaves its text where it was.
public class ListKeyTests
{
    const string styles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Heading1\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Steps\"><w:name w:val=\"Steps\"/><w:pPr><w:numPr><w:ilvl w:val=\"2\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style>";

    [Test]
    public async Task TheOutline_SaysWhatListLevelAParagraphIsAt()
    {
        var docx = DocumentEditor.SetList(Build(P(R("Plain")) + P(R("Listed")) + P(R("Deeper")), styles: styles), [1, 2], ListKind.Bullet, Plain);
        docx = DocumentEditor.Indent(docx, [2], 1, Plain);

        var paragraphs = DocumentOutline.Read(docx).Paragraphs;

        await Assert.That(paragraphs.Select(_ => _.ListLevel)).IsEquivalentTo(new int?[] {null, 0, 1});
    }

    // A style can put its paragraphs in a list; numbering 0 on the paragraph takes it out again.
    [Test]
    public async Task TheOutline_ReadsAListLevelFromTheStyle_UnlessTheParagraphLeavesIt()
    {
        var docx = Build(
            "<w:p><w:pPr><w:pStyle w:val=\"Steps\"/></w:pPr><w:r><w:t>Styled</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Steps\"/><w:numPr><w:numId w:val=\"0\"/></w:numPr></w:pPr><w:r><w:t>Left</w:t></w:r></w:p>",
            styles: styles);

        var paragraphs = DocumentOutline.Read(docx).Paragraphs;

        await Assert.That(paragraphs.Select(_ => _.ListLevel)).IsEquivalentTo(new int?[] {2, null});
    }

    // w:next is a style's own: Heading 2 here names none, and Heading 1's is not inherited.
    [Test]
    public async Task TheOutline_SaysWhatStyleFollowsAParagraph_WhenItIsAnother()
    {
        var docx = Build(
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>One</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"Heading2\"/></w:pPr><w:r><w:t>Two</w:t></w:r></w:p>" +
            P(R("Body")),
            styles: styles);

        var paragraphs = DocumentOutline.Read(docx).Paragraphs;

        await Assert.That(paragraphs.Select(_ => _.NextStyle)).IsEquivalentTo(["Normal", null, null]);
    }

    // Word's level 1 is indented 0.5" with a 0.25" hanging indent for its bullet: the text stands at
    // 0.5", and stays there.
    [Test]
    public async Task Unnumber_LeavesTheTextWhereItWas()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);
        docx = DocumentEditor.Indent(docx, [1], 1, Plain);

        var first = DocumentEditor.Unnumber(docx, 0, Plain);
        var second = DocumentEditor.Unnumber(docx, 1, Plain);

        await Assert.That(Xml(first)).StartsWith("<w:p><w:pPr><w:ind w:left=\"720\" /></w:pPr><w:r>");
        await Assert.That(Xml(second)).EndsWith("<w:p><w:pPr><w:ind w:left=\"1440\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
        await Assert.That(DocumentOutline.Read(first).Paragraphs.Select(_ => _.ListLevel)).IsEquivalentTo(new int?[] {null, 1});
    }

    // A paragraph's own indent is where its text is, whatever its level says.
    [Test]
    public async Task Unnumber_KeepsTheParagraphsOwnIndent()
    {
        var docx = Build(
            "<w:p><w:pPr><w:pStyle w:val=\"Steps\"/><w:ind w:left=\"2000\" w:hanging=\"400\"/></w:pPr><w:r><w:t>Own</w:t></w:r></w:p>",
            styles: styles);

        var edited = DocumentEditor.Unnumber(docx, 0, Plain);

        await Assert.That(Xml(edited)).IsEqualTo(
            "<w:p><w:pPr><w:pStyle w:val=\"Steps\" /><w:numPr><w:numId w:val=\"0\" /></w:numPr><w:ind w:left=\"2000\" /></w:pPr><w:r><w:t>Own</w:t></w:r></w:p>");
    }

    [Test]
    public async Task Unnumber_OutOfAList_ChangesNothing()
    {
        var docx = Build(P(R("Plain")), styles: styles);

        await Assert.That(Xml(DocumentEditor.Unnumber(docx, 0, Plain))).IsEqualTo(Xml(docx));
    }

    [Test]
    public async Task Unnumber_Tracked_IsAParagraphFormattingChange_ThatRejectingTakesBack()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")), styles: styles), [0], ListKind.Number, Plain);

        var edited = DocumentEditor.Unnumber(docx, 0, Tracked);

        await Assert.That(DocumentReview.Read(edited).Changes.Select(_ => _.Kind)).IsEquivalentTo([ReviewChangeKind.ParagraphFormatting]);
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Xml(docx));
    }
}
