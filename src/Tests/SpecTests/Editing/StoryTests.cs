using static EditDocuments;
using static ReviewDocuments;

// Headers, footers and footnotes edited as the body is: their paragraphs numbered after the body's,
// made when a section has none, and written into as Word writes them (Word-probed 2026-10-19, see
// DocumentEditor.Stories.cs).
public class StoryTests
{
    const string section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";

    static byte[] Document() =>
        Build(P(R("Body one")) + P(R("Body two")) + section);

    static string Header(byte[] docx) =>
        Bare(Read(docx, _ => _.MainDocumentPart!.HeaderParts.Single().Header!.InnerXml));

    static string Footer(byte[] docx) =>
        Bare(Read(docx, _ => _.MainDocumentPart!.FooterParts.Single().Footer!.InnerXml));

    static string Footnotes(byte[] docx) =>
        Bare(Read(docx, _ => _.MainDocumentPart!.FootnotesPart!.Footnotes!.InnerXml));

    // A section with no header is given one: a part of its own, in the Header style, named by the
    // section. The place returned is its paragraph, numbered after the body's.
    [Test]
    public async Task AHeader_IsMadeWhereThereIsNone()
    {
        var result = DocumentEditor.EditHeader(Document(), 0, false, Plain);

        await Assert.That(Header(result.Document)).IsEqualTo("<w:p><w:pPr><w:pStyle w:val=\"Header\" /></w:pPr></w:p>");
        await Assert.That(Xml(result.Document)).Contains("<w:sectPr><w:headerReference xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" w:type=\"default\" r:id=\"rId1\" /><w:pgSz");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((2, 0));
        await Assert.That(StyleIds(result.Document)).Contains("Header");

        var outline = DocumentOutline.Read(result.Document);
        await Assert.That(outline.Paragraphs.Select(_ => _.Part)).IsEquivalentTo([ReviewPart.Body, ReviewPart.Body, ReviewPart.Header]);
    }

    // Asked again, it is the one there is.
    [Test]
    public async Task AHeader_ThatIsThere_IsOpened()
    {
        var made = DocumentEditor.EditHeader(Document(), 0, false, Plain).Document;

        var again = DocumentEditor.EditHeader(made, 1, false, Plain);

        await Assert.That(again.Document.Length).IsGreaterThan(0);
        await Assert.That(Read(again.Document, _ => _.MainDocumentPart!.HeaderParts.Count())).IsEqualTo(1);
        await Assert.That(again.Paragraph).IsEqualTo(2);
    }

    // A section that names no header shows the one before it, and that is the one opened.
    [Test]
    public async Task ASectionWithoutItsOwnHeader_OpensTheOneItShows()
    {
        var made = DocumentEditor.EditHeader(Document(), 0, false, Plain).Document;
        var two = DocumentEditor.InsertSectionBreak(made, 0, 8, false, Plain).Document;

        var second = DocumentEditor.EditHeader(two, 1, false, Plain);

        await Assert.That(Read(second.Document, _ => _.MainDocumentPart!.HeaderParts.Count())).IsEqualTo(1);
        await Assert.That(DocumentOutline.Read(second.Document).Paragraphs[second.Paragraph].Part).IsEqualTo(ReviewPart.Header);
    }

    [Test]
    public async Task AFooter_IsMadeAsAHeaderIs()
    {
        var result = DocumentEditor.EditHeader(Document(), 0, true, Plain);

        await Assert.That(Footer(result.Document)).IsEqualTo("<w:p><w:pPr><w:pStyle w:val=\"Footer\" /></w:pPr></w:p>");
        await Assert.That(Xml(result.Document)).Contains("<w:sectPr><w:footerReference xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" w:type=\"default\" r:id=\"rId1\" /><w:pgSz");
    }

    // Typed into, a header's paragraph is written in its own part.
    [Test]
    public async Task AHeader_IsTypedIntoAsTheBodyIs()
    {
        var made = DocumentEditor.EditHeader(Document(), 0, false, Plain);

        var typed = Rewrite(made.Document, made.Paragraph, Plain, Reads(T(-1, "Report")));

        await Assert.That(Header(typed)).IsEqualTo("<w:p><w:pPr><w:pStyle w:val=\"Header\" /></w:pPr><w:r><w:t xml:space=\"preserve\">Report</w:t></w:r></w:p>");
    }

    // A link in a header is the header part's relationship, not the body's.
    [Test]
    public async Task ALinkInAHeader_IsTheHeadersOwn()
    {
        var made = DocumentEditor.EditHeader(Document(), 0, false, Plain);
        var typed = Rewrite(made.Document, made.Paragraph, Plain, Reads(T(-1, "Site")));
        var outline = DocumentOutline.Read(typed);
        var header = outline.Paragraphs[made.Paragraph];

        var linked = DocumentEditor.Link(typed, header.PositionAt(0, ending: false)!.Value, header.PositionAt(4, ending: true)!.Value, "https://example.com/", Plain);

        await Assert.That(Read(linked, _ => _.MainDocumentPart!.HeaderParts.Single().HyperlinkRelationships.Single().Uri.OriginalString)).IsEqualTo("https://example.com/");
        await Assert.That(Read(linked, _ => _.MainDocumentPart!.HyperlinkRelationships.Count())).IsEqualTo(0);
    }

    // A footnote is its reference at the place and its note, with Word's separators first; the place
    // returned is its note's paragraph, after the note's mark and a space.
    [Test]
    public async Task AFootnote_IsItsReference_AndItsNote()
    {
        var result = DocumentEditor.InsertFootnote(Document(), 0, 4, Plain);

        await Assert.That(Xml(result.Document)).StartsWith(
            "<w:p><w:r><w:t xml:space=\"preserve\">Body</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\" /></w:rPr><w:footnoteReference w:id=\"1\" /></w:r><w:r><w:t xml:space=\"preserve\"> one</w:t></w:r></w:p>");
        var notes = Footnotes(result.Document);
        await Assert.That(notes).StartsWith("<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\" /></w:pPr><w:r><w:separator /></w:r></w:p></w:footnote>");
        await Assert.That(notes).EndsWith(
            "<w:footnote w:id=\"1\"><w:p><w:pPr><w:pStyle w:val=\"FootnoteText\" /></w:pPr><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\" /></w:rPr><w:footnoteRef /></w:r><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p></w:footnote>");

        var outline = DocumentOutline.Read(result.Document);
        await Assert.That(outline.Paragraphs[result.Paragraph].Part).IsEqualTo(ReviewPart.Footnotes);
    }

    // A second footnote is the next number, and its note is written into as the body is.
    [Test]
    public async Task ASecondFootnote_IsTheNextNumber()
    {
        var first = DocumentEditor.InsertFootnote(Document(), 0, 4, Plain);
        var second = DocumentEditor.InsertFootnote(first.Document, 1, 8, Plain);
        var note = DocumentOutline.Read(second.Document).Paragraphs[second.Paragraph];

        // Typed at its end, as the editor hands it in: after the space, in the space's run.
        var last = note.Units.Count - 1;
        var typed = Rewrite(second.Document, second.Paragraph, Plain, Reads([.. note.Units.Select((unit, index) => unit.Editable ? T(index, index == last ? unit.Text + "A note." : unit.Text) : K(index))]));

        await Assert.That(Footnotes(typed)).Contains("<w:footnoteRef /></w:r><w:r><w:t xml:space=\"preserve\"> A note.</w:t></w:r></w:p></w:footnote>");
        await Assert.That(Xml(typed)).Contains("<w:footnoteReference w:id=\"2\" />");
    }

    [Test]
    public async Task AFootnote_Tracked_IsAnInsertion()
    {
        var result = DocumentEditor.InsertFootnote(Document(), 0, 4, Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Changes.Select(_ => _.Kind)).Contains(ReviewChangeKind.Insertion);
        await Assert.That(Footnotes(result.Document)).Contains("<w:ins ");
    }

    // An endnote is written as a footnote is (Word-probed 2026-10-21): its reference in the Endnote
    // Reference style, its note in the endnotes part after Word's separators, in Endnote Text.
    [Test]
    public async Task AnEndnote_IsItsReference_AndItsNote()
    {
        var result = DocumentEditor.InsertEndnote(Document(), 0, 4, Plain);

        await Assert.That(Xml(result.Document)).StartsWith(
            "<w:p><w:r><w:t xml:space=\"preserve\">Body</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"EndnoteReference\" /></w:rPr><w:endnoteReference w:id=\"1\" /></w:r><w:r><w:t xml:space=\"preserve\"> one</w:t></w:r></w:p>");
        var notes = Endnotes(result.Document);
        await Assert.That(notes).StartsWith("<w:endnote w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\" /></w:pPr><w:r><w:separator /></w:r></w:p></w:endnote>");
        await Assert.That(notes).EndsWith(
            "<w:endnote w:id=\"1\"><w:p><w:pPr><w:pStyle w:val=\"EndnoteText\" /></w:pPr><w:r><w:rPr><w:rStyle w:val=\"EndnoteReference\" /></w:rPr><w:endnoteRef /></w:r><w:r><w:t xml:space=\"preserve\"> </w:t></w:r></w:p></w:endnote>");
        await Assert.That(StyleIds(result.Document)).Contains("EndnoteText");
        await Assert.That(StyleIds(result.Document)).Contains("EndnoteReference");

        var outline = DocumentOutline.Read(result.Document);
        await Assert.That(outline.Paragraphs[result.Paragraph].Part).IsEqualTo(ReviewPart.Endnotes);
    }

    // Word names each kind of note's separators in the settings, footnotes before endnotes, both
    // before the compatibility settings.
    [Test]
    public async Task TheSeparators_AreNamedInTheSettings()
    {
        var footnoted = DocumentEditor.InsertFootnote(WithSettings(Document()), 0, 4, Plain);
        var both = DocumentEditor.InsertEndnote(footnoted.Document, 0, 2, Plain);

        await Assert.That(Bare(Read(both.Document, _ => _.MainDocumentPart!.DocumentSettingsPart!.Settings!.InnerXml))).IsEqualTo(
            "<w:footnotePr><w:footnote w:id=\"-1\" /><w:footnote w:id=\"0\" /></w:footnotePr><w:endnotePr><w:endnote w:id=\"-1\" /><w:endnote w:id=\"0\" /></w:endnotePr><w:compat />");
    }

    static string Endnotes(byte[] docx) =>
        Bare(Read(docx, _ => _.MainDocumentPart!.EndnotesPart!.Endnotes!.InnerXml));

    static byte[] WithSettings(byte[] docx)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, true))
        {
            package.MainDocumentPart!.AddNewPart<DocumentFormat.OpenXml.Packaging.DocumentSettingsPart>().Settings =
                new(new DocumentFormat.OpenXml.Wordprocessing.Compatibility());
        }

        return stream.ToArray();
    }

    [Test]
    public async Task AFootnote_InAHeader_IsRefused()
    {
        var made = DocumentEditor.EditHeader(Document(), 0, false, Plain);

        await Assert.That(() => DocumentEditor.InsertFootnote(made.Document, made.Paragraph, 0, Plain)).Throws<InvalidOperationException>();
    }

    static string StyleIds(byte[] docx) =>
        string.Join(
            ',',
            Read(docx, _ => _.MainDocumentPart!.StyleDefinitionsPart?.Styles?.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().Select(style => style.StyleId!.Value).ToList() ?? []));
}
