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
    // 0.5", and stays there. As Word writes it, the paragraph keeps List Paragraph, which puts its
    // text at 0.5" already; at level 2 it is given 1" of its own.
    [Test]
    public async Task Unnumber_LeavesTheTextWhereItWas()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);
        docx = DocumentEditor.Indent(docx, [1], 1, Plain);

        var first = DocumentEditor.Unnumber(docx, 0, Plain);
        var second = DocumentEditor.Unnumber(docx, 1, Plain);

        await Assert.That(Xml(first)).StartsWith("<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /></w:pPr><w:r>");
        await Assert.That(Xml(second)).EndsWith("<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /><w:ind w:left=\"1440\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
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

    static string Levels(byte[] docx) =>
        string.Join(" ", Read(docx, _ => _.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!
            .Elements<DocumentFormat.OpenXml.Wordprocessing.AbstractNum>()
            .Select(definition => string.Join(",", definition.Elements<DocumentFormat.OpenXml.Wordprocessing.Level>()
                .Take(3)
                .Select(level => level.PreviousParagraphProperties!.Indentation!.Left!.Value)))
            .ToList()));

    static IReadOnlyList<int?> ListLevels(byte[] docx) =>
        DocumentOutline.Read(docx).Paragraphs.Select(_ => _.ListLevel).ToList();

    // Backspace, again and again, at the start of a bulleted paragraph under another, as Word steps
    // it: the bullet goes; the text goes to where the bullet above stands; then to the margin; then
    // the two paragraphs join, which is the editor's to do once nothing is left to take off.
    [Test]
    public async Task Unindent_StepsAsWordDoes_UnderABullet()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);

        var once = DocumentEditor.Unindent(docx, 1, Plain);
        var twice = DocumentEditor.Unindent(once, 1, Plain);
        var thrice = DocumentEditor.Unindent(twice, 1, Plain);

        await Assert.That(Xml(once)).EndsWith("<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
        await Assert.That(Xml(twice)).EndsWith("<w:p><w:pPr><w:ind w:left=\"360\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
        await Assert.That(Xml(thrice)).EndsWith("<w:p><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
        await Assert.That(DocumentOutline.Read(thrice).Paragraphs.Select(_ => _.Indented)).IsEquivalentTo([true, false]);
        await Assert.That(Xml(DocumentEditor.Unindent(thrice, 1, Plain))).IsEqualTo(Xml(thrice));
    }

    // At level 2 the text, at 1", is not where the bullet above it has its text: its indent goes at once.
    [Test]
    public async Task Unindent_AtLevel2_GoesToTheMarginNext()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);
        docx = DocumentEditor.Indent(docx, [1], 1, Plain);

        var once = DocumentEditor.Unindent(docx, 1, Plain);
        var twice = DocumentEditor.Unindent(once, 1, Plain);

        await Assert.That(Xml(once)).EndsWith("<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\" /><w:ind w:left=\"1440\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
        await Assert.That(Xml(twice)).EndsWith("<w:p><w:r><w:t xml:space=\"preserve\">Two</w:t></w:r></w:p>");
    }

    // An indent of the paragraph's own goes at once, whatever it is; one its style gives it is
    // overridden. Word-probed with 36pt, 72pt, 100pt and 144pt, a first-line and a hanging indent.
    [Test]
    [Arguments("<w:ind w:left=\"2000\"/>", "")]
    [Arguments("<w:ind w:firstLine=\"720\"/>", "")]
    [Arguments("<w:ind w:left=\"1440\" w:hanging=\"720\"/>", "")]
    [Arguments("<w:pStyle w:val=\"Indented\"/>", "<w:pStyle w:val=\"Indented\" /><w:ind w:left=\"0\" />")]
    [Arguments("<w:pStyle w:val=\"FirstLine\"/>", "<w:pStyle w:val=\"FirstLine\" /><w:ind w:firstLine=\"0\" />")]
    [Arguments("<w:pStyle w:val=\"Indented\"/><w:ind w:left=\"2160\"/>", "<w:pStyle w:val=\"Indented\" />")]
    public async Task Unindent_TakesOffOneStep(string properties, string expected)
    {
        const string indenting =
            "<w:style w:type=\"paragraph\" w:styleId=\"Indented\"><w:name w:val=\"Indented\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"1440\"/></w:pPr></w:style>" +
            "<w:style w:type=\"paragraph\" w:styleId=\"FirstLine\"><w:name w:val=\"FirstLine\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:firstLine=\"720\"/></w:pPr></w:style>";
        var docx = Build(P(R("Before")) + $"<w:p><w:pPr>{properties}</w:pPr><w:r><w:t>Target</w:t></w:r></w:p>", styles: styles + indenting);

        var edited = DocumentEditor.Unindent(docx, 1, Plain);

        var mark = expected.Length > 0 ? $"<w:pPr>{expected}</w:pPr>" : "";
        await Assert.That(Xml(edited)).EndsWith($"<w:p>{mark}<w:r><w:t>Target</w:t></w:r></w:p>");
    }

    [Test]
    public async Task Unindent_Tracked_IsAParagraphFormattingChange()
    {
        var docx = Build(P(R("Before")) + "<w:p><w:pPr><w:ind w:left=\"720\"/></w:pPr><w:r><w:t>Target</w:t></w:r></w:p>", styles: styles);

        var edited = DocumentEditor.Unindent(docx, 1, Tracked);

        await Assert.That(DocumentReview.Read(edited).Changes.Select(_ => _.Kind)).IsEquivalentTo([ReviewChangeKind.ParagraphFormatting]);
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Xml(docx));
    }

    // Tab in a list's first paragraph moves the whole list a quarter inch in, in its definition; in
    // any other paragraph it moves that paragraph a level.
    [Test]
    public async Task TabInList_AtTheFirstParagraph_MovesTheWholeList()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);

        var listed = DocumentEditor.TabInList(docx, [0], 1, Plain);
        var second = DocumentEditor.TabInList(listed, [1], 1, Plain);

        await Assert.That(Levels(docx)).IsEqualTo("720,1440,2160");
        await Assert.That(Levels(listed)).IsEqualTo("1080,1800,2520");
        await Assert.That(ListLevels(listed)).IsEquivalentTo(new int?[] {0, 0});
        await Assert.That(ListLevels(second)).IsEquivalentTo(new int?[] {0, 1});
        await Assert.That(Levels(second)).IsEqualTo("1080,1800,2520");
    }

    // Shift+Tab moves it back out, until its first bullet stands at the margin.
    [Test]
    public async Task ShiftTabInList_AtTheFirstParagraph_StopsAtTheMargin()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);

        var once = DocumentEditor.TabInList(docx, [0], -1, Plain);
        var twice = DocumentEditor.TabInList(once, [0], -1, Plain);

        await Assert.That(Levels(once)).IsEqualTo("360,1080,1800");
        await Assert.That(Levels(twice)).IsEqualTo("360,1080,1800");
    }

    // Word records nothing of it, tracking or not: a list's definition is not a tracked thing.
    [Test]
    public async Task TabInList_Tracked_IsNotTracked()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Two")), styles: styles), [0, 1], ListKind.Bullet, Plain);

        var edited = DocumentEditor.TabInList(docx, [0], 1, Tracked);

        await Assert.That(Levels(edited)).IsEqualTo("1080,1800,2520");
        await Assert.That(DocumentReview.Read(edited).Changes).IsEmpty();
    }

    // A definition two lists share is given to the list moved as its own, so the other stays.
    [Test]
    public async Task TabInList_ADefinitionAnotherListShares_IsCopied()
    {
        var docx = DocumentEditor.SetList(Build(P(R("One")) + P(R("Between")) + P(R("Other")), styles: styles), [0], ListKind.Bullet, Plain);
        docx = Share(docx);

        var edited = DocumentEditor.TabInList(docx, [0], 1, Plain);

        await Assert.That(Levels(edited)).IsEqualTo("720,1440,2160 1080,1800,2520");
        await Assert.That(ListLevels(edited)).IsEquivalentTo(new int?[] {0, null, 0});
    }

    // A second list on the first's definition, for the paragraph "Other".
    static byte[] Share(byte[] docx)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, true))
        {
            var numbering = package.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
            var first = numbering.Elements<DocumentFormat.OpenXml.Wordprocessing.NumberingInstance>().Single();
            var second = (DocumentFormat.OpenXml.Wordprocessing.NumberingInstance) first.CloneNode(true);
            second.NumberID = 2;
            first.InsertAfterSelf(second);
            var other = package.MainDocumentPart.Document!.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Last();
            other.PrependChild(new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                new DocumentFormat.OpenXml.Wordprocessing.NumberingProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.NumberingLevelReference {Val = 0},
                    new DocumentFormat.OpenXml.Wordprocessing.NumberingId {Val = 2})));
        }

        return stream.ToArray();
    }
}
