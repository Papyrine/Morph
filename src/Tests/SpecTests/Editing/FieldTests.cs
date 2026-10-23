using static EditDocuments;
using static ReviewDocuments;

// The fields DocumentEditor puts in at the caret — page number, number of pages, date — and the table
// of contents, asserted on the markup Word was probed writing (DocumentEditor.Fields.cs).
public class FieldTests
{
    const string section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";

    static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\" /></w:r>" +
        $"<w:r><w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\" /></w:r>" +
        $"<w:r><w:rPr><w:noProof /></w:rPr><w:t xml:space=\"preserve\">{result}</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\" /></w:r>";

    static string Body(byte[] docx) =>
        Xml(docx).Replace(Bare(Read(docx, _ => _.MainDocumentPart!.Document!.Body!.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>()?.OuterXml ?? "")), "");

    // Word-probed 2026-10-23: Insert > Page Number > Current Position > Plain Number.
    [Test]
    public async Task APageNumber_IsAPageField_AtTheCaret()
    {
        var docx = Build(P(R("Page here")) + section);

        var result = DocumentEditor.InsertPageNumber(docx, 0, 5, false, Plain);

        await Assert.That(Body(result.Document)).IsEqualTo(
            "<w:p><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r>" +
            Field(" PAGE   \\* MERGEFORMAT ", "1") +
            "<w:r><w:t xml:space=\"preserve\">here</w:t></w:r></w:p>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((0, 6));
    }

    [Test]
    public async Task TheNumberOfPages_IsANumPagesField()
    {
        var docx = Build(P(R("of ")) + section);

        var result = DocumentEditor.InsertPageNumber(docx, 0, 3, true, Plain);

        await Assert.That(Body(result.Document)).Contains(Field(" NUMPAGES   \\* MERGEFORMAT ", "1"));
    }

    // Word-probed 2026-10-23: Insert > Date and Time, Update automatically.
    [Test]
    public async Task ADate_ThatUpdates_IsADateField_ShowingTheDate()
    {
        var docx = Build(P(R("Dated ")) + section);

        var result = DocumentEditor.InsertDate(docx, 0, 6, "d MMMM yyyy", new(2026, 10, 23), true, Plain);

        await Assert.That(Body(result.Document)).Contains(Field(" DATE \\@ \"d MMMM yyyy\" ", "23 October 2026"));
        await Assert.That(result.Offset).IsEqualTo(7);
    }

    [Test]
    public async Task ADate_ThatDoesNotUpdate_IsText()
    {
        var docx = Build(P(R("Dated ")) + section);

        var result = DocumentEditor.InsertDate(docx, 0, 6, "yyyy-MM-dd", new(2026, 10, 23), false, Plain);

        await Assert.That(Body(result.Document)).IsEqualTo(
            "<w:p><w:r><w:t xml:space=\"preserve\">Dated </w:t></w:r><w:r><w:t xml:space=\"preserve\">2026-10-23</w:t></w:r></w:p>");
        await Assert.That(result.Offset).IsEqualTo(16);
    }

    [Test]
    public async Task AField_Tracked_IsOneInsertion()
    {
        var docx = Build(P(R("Page ")) + section);

        var result = DocumentEditor.InsertPageNumber(docx, 0, 5, false, Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Changes).Count().IsEqualTo(1);
        await Assert.That(Body(result.Document)).Contains("<w:ins ");
    }

    // A document of a title, two headings of Word's own Heading 1 and 2, and body text.
    static byte[] Headed()
    {
        var docx = Build(P(R("Title")) + P(R("Chapter one")) + P(R("Body")) + P(R("Section two")) + section);
        docx = DocumentEditor.SetStyle(docx, [1], "Heading1", Plain);
        return DocumentEditor.SetStyle(docx, [3], "Heading2", Plain);
    }

    // Word-probed 2026-10-23: References > Table of Contents > Automatic Table 1.
    [Test]
    public async Task AContentsTable_IsWordsAutomaticTable()
    {
        var result = DocumentEditor.InsertContents(Headed(), 0, 0, Plain);

        var body = Body(result.Document);
        await Assert.That(body).StartsWith(
            "<w:sdt><w:sdtPr><w:id w:val=\"1\" /><w:docPartObj><w:docPartGallery w:val=\"Table of Contents\" /><w:docPartUnique /></w:docPartObj></w:sdtPr>" +
            "<w:sdtContent><w:p><w:pPr><w:pStyle w:val=\"TOCHeading\" /></w:pPr><w:r><w:t>Contents</w:t></w:r></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"TOC1\" /><w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9016\" /></w:tabs><w:rPr><w:noProof /></w:rPr></w:pPr>" +
            "<w:r><w:fldChar w:fldCharType=\"begin\" /></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\" /></w:r>" +
            "<w:hyperlink w:history=\"1\" w:anchor=\"_Toc200000001\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\" /><w:noProof /></w:rPr><w:t xml:space=\"preserve\">Chapter one</w:t></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:tab /></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:fldChar w:fldCharType=\"begin\" /></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:instrText xml:space=\"preserve\"> PAGEREF _Toc200000001 \\h </w:instrText></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:fldChar w:fldCharType=\"separate\" /></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:t>1</w:t></w:r>" +
            "<w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:fldChar w:fldCharType=\"end\" /></w:r></w:hyperlink></w:p>" +
            "<w:p><w:pPr><w:pStyle w:val=\"TOC2\" />");
        await Assert.That(body).Contains(
            "<w:p><w:r><w:rPr><w:b /><w:bCs /><w:noProof /></w:rPr><w:fldChar w:fldCharType=\"end\" /></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:p><w:r><w:t xml:space=\"preserve\">Title</w:t></w:r></w:p>");
        await Assert.That(body).Contains(
            "<w:bookmarkStart w:name=\"_Toc200000001\" w:id=\"0\" /><w:r><w:t xml:space=\"preserve\">Chapter one</w:t></w:r><w:bookmarkEnd w:id=\"0\" />");
        await Assert.That(body).Contains("w:name=\"_Toc200000002\" w:id=\"1\" /><w:r><w:t xml:space=\"preserve\">Section two</w:t></w:r>");

        var styles = Read(result.Document, _ => _.MainDocumentPart!.StyleDefinitionsPart!.Styles!.OuterXml);
        foreach (var style in new[] {"TOCHeading", "TOC1", "TOC2", "Hyperlink"})
        {
            await Assert.That(styles).Contains($"w:styleId=\"{style}\"");
        }

        // The caret goes on in the paragraph after the table: its heading, two entries and the
        // paragraph that ends its field come first.
        await Assert.That(result.Paragraph).IsEqualTo(4);
    }

    // A heading with its own outline level is listed too (\u), and body text is not (level 9).
    [Test]
    public async Task AContentsTable_ListsOutlineLevels_NotBodyText()
    {
        var docx = Build(
            P(R("Top")) +
            P("<w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr>", R("Own level")) +
            P("<w:pPr><w:outlineLvl w:val=\"9\"/></w:pPr>", R("Body level")) +
            P("<w:pPr><w:outlineLvl w:val=\"3\"/></w:pPr>", R("Too deep")) +
            section);

        var result = DocumentEditor.InsertContents(docx, 0, 0, Plain);

        var body = Body(result.Document);
        await Assert.That(body).Contains(">Own level</w:t>");
        await Assert.That(body.Split(">Body level</w:t>").Length).IsEqualTo(2);
        await Assert.That(body.Split(">Too deep</w:t>").Length).IsEqualTo(2);
    }

    [Test]
    public async Task AContentsTable_WithNoHeadings_SaysSo()
    {
        var docx = Build(P(R("Nothing to list")) + section);

        var result = DocumentEditor.InsertContents(docx, 0, 0, Plain);

        await Assert.That(Body(result.Document)).Contains(
            "<w:r><w:rPr><w:b /><w:bCs /><w:noProof /></w:rPr><w:t>No table of contents entries found.</w:t></w:r>" +
            "<w:r><w:rPr><w:b /><w:bCs /><w:noProof /></w:rPr><w:fldChar w:fldCharType=\"end\" /></w:r>");
    }

    // The pages are the layout's to say: each entry takes the page of the paragraph its bookmark marks.
    // After the table come Title (4), Chapter one (5), Body (6) and Section two (7).
    [Test]
    public async Task ContentsPages_AreThePagesOfTheHeadings()
    {
        var inserted = DocumentEditor.InsertContents(Headed(), 0, 0, Plain).Document;
        var chapter = 5;
        var section2 = 7;

        var paged = DocumentEditor.SetContentsPages(inserted, _ => _ == chapter ? 2 : _ == section2 ? 13 : null);

        var body = Body(paged);
        await Assert.That(body).Contains("PAGEREF _Toc200000001 \\h </w:instrText></w:r><w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr></w:r><w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:fldChar w:fldCharType=\"separate\" /></w:r><w:r><w:rPr><w:noProof /><w:webHidden /></w:rPr><w:t>2</w:t></w:r>");
        await Assert.That(body).Contains("<w:t>13</w:t>");
    }

    // Update Table lists the headings as they now are — one renamed, one new — keeping the bookmark
    // of the one already listed, and the table's heading.
    [Test]
    public async Task UpdateContents_ListsTheHeadingsAsTheyAre()
    {
        var inserted = DocumentEditor.InsertContents(Headed(), 0, 0, Plain).Document;
        var chapter = 5;
        var renamed = Rewrite(inserted, chapter, Plain, new NewParagraph([new(-1, "Chapter the first")]));
        var body = 6;
        var added = DocumentEditor.SetStyle(renamed, [body], "Heading1", Plain);
        await Assert.That(DocumentEditor.HasContents(added)).IsTrue();

        var updated = DocumentEditor.UpdateContents(added, Plain);

        var xml = Body(updated);
        await Assert.That(xml).Contains(">Contents</w:t>");
        await Assert.That(xml).Contains("w:history=\"1\" w:anchor=\"_Toc200000001\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\" /><w:noProof /></w:rPr><w:t xml:space=\"preserve\">Chapter the first</w:t>");
        await Assert.That(xml).Contains("w:anchor=\"_Toc200000003\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\" /><w:noProof /></w:rPr><w:t xml:space=\"preserve\">Body</w:t>");
        await Assert.That(xml.Split(" TOC \\o").Length).IsEqualTo(2);
        await Assert.That(xml.Split("Chapter one").Length).IsEqualTo(1);
    }

    [Test]
    public async Task AContentsTable_Tracked_IsReviewable()
    {
        var result = DocumentEditor.InsertContents(Headed(), 0, 0, Tracked);

        await Assert.That(DocumentReview.Read(result.Document).Changes.Count).IsGreaterThan(0);
        await Assert.That(DocumentReview.Read(ReviewEditor.ResolveAll(result.Document, false)).Changes).IsEmpty();
    }
}
