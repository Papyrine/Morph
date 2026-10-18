using static EditDocuments;
using static ReviewDocuments;

// What DocumentEditor puts into a document's text — page breaks, tables, pictures, links — and the
// text it replaces, asserted on the markup it leaves.
public class InsertTests
{
    // A4, as Word's blank document on the probing machine has it: 9026 twips between the margins.
    const string section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";

    const string pageBreak = "<w:p><w:r><w:br w:type=\"page\" /></w:r></w:p>";

    static string Body(byte[] docx) =>
        Xml(docx).Replace(Bare(Read(docx, _ => _.MainDocumentPart!.Document!.Body!.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>()?.OuterXml ?? "")), "");

    // Page breaks

    // Word-probed 2026-10-18: the break is a paragraph of its own between the two halves.
    [Test]
    public async Task APageBreak_InTheMiddle_IsAParagraphBetweenTheHalves()
    {
        var docx = Build(P(R("Break here")) + section);

        var result = DocumentEditor.InsertPageBreak(docx, 0, 5, Plain);

        await Assert.That(Body(result.Document)).IsEqualTo(
            "<w:p><w:r><w:t xml:space=\"preserve\">Break</w:t></w:r></w:p>" +
            pageBreak +
            "<w:p><w:r><w:t xml:space=\"preserve\"> here</w:t></w:r></w:p>");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((2, 0));
    }

    [Test]
    public async Task APageBreak_AtTheStart_GoesBefore_AndAtTheEnd_LeavesAnEmptyParagraphAfter()
    {
        var docx = Build(P(R("Text")) + section);

        var start = DocumentEditor.InsertPageBreak(docx, 0, 0, Plain);
        var end = DocumentEditor.InsertPageBreak(docx, 0, 4, Plain);

        await Assert.That(Body(start.Document)).IsEqualTo(pageBreak + "<w:p><w:r><w:t xml:space=\"preserve\">Text</w:t></w:r></w:p>");
        await Assert.That(Body(end.Document)).IsEqualTo("<w:p><w:r><w:t xml:space=\"preserve\">Text</w:t></w:r></w:p>" + pageBreak + "<w:p />");
    }

    [Test]
    public async Task APageBreak_Tracked_IsAnInsertion()
    {
        var docx = Build(P(R("Break here")) + section);

        var result = DocumentEditor.InsertPageBreak(docx, 0, 5, Tracked);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Break] ¶+ {+[]+} ¶+ [ here] ¶");
        await Assert.That(DocumentReview.Read(result.Document).Changes).Count().IsEqualTo(1);
    }

    // Tables

    // Word-probed 2026-10-18: a 3-column table inserted on A4 has columns of 3005, 3005 and 3006
    // twips — the 9026 between the margins less Table Grid's 0.5pt border, the last taking what is left.
    [Test]
    public async Task ATable_StandsBetweenTheHalves_AsWideAsTheText()
    {
        var docx = Build(P(R("Table here")) + section);

        var result = DocumentEditor.InsertTable(docx, 0, 5, 2, 3, Plain);

        var body = Body(result.Document);
        await Assert.That(body).StartsWith(
            "<w:p><w:r><w:t xml:space=\"preserve\">Table</w:t></w:r></w:p>" +
            "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\" /><w:tblW w:w=\"0\" w:type=\"auto\" /><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\" /></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"3005\" /><w:gridCol w:w=\"3005\" /><w:gridCol w:w=\"3006\" /></w:tblGrid>" +
            "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3005\" w:type=\"dxa\" /></w:tcPr><w:p /></w:tc>");
        await Assert.That(body).EndsWith("</w:tbl><w:p><w:r><w:t xml:space=\"preserve\"> here</w:t></w:r></w:p>");
        await Assert.That(Read(result.Document, _ => _.MainDocumentPart!.Document!.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Count())).IsEqualTo(2);
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((1, 0));
        await Assert.That(Read(result.Document, _ => _.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().Select(style => style.StyleId!.Value!).ToList())).IsEquivalentTo(["TableGrid", "TableNormal"]);
    }

    // Word-probed 2026-10-18: at an empty paragraph the table goes before it, and the paragraph
    // stays after the table.
    [Test]
    public async Task ATable_AtAParagraphsStart_GoesBeforeIt()
    {
        var docx = Build("<w:p/>" + section);

        var result = DocumentEditor.InsertTable(docx, 0, 0, 1, 2, Plain);

        await Assert.That(Body(result.Document)).StartsWith("<w:tbl>");
        await Assert.That(Body(result.Document)).EndsWith("</w:tbl><w:p />");
    }

    [Test]
    public async Task ATable_Tracked_IsRowsAndMarksInserted()
    {
        var docx = Build(P(R("Text")) + section);

        var result = DocumentEditor.InsertTable(docx, 0, 4, 2, 2, Tracked);

        var changes = DocumentReview.Read(result.Document).Changes.Select(_ => _.Kind).ToList();
        await Assert.That(changes).Contains(ReviewChangeKind.RowInsertion);
        // The table goes; the paragraph it divided stays divided, its new mark settled while the
        // table still stood between the halves.
        var rejected = ReviewEditor.ResolveAll(result.Document, accept: false);
        await Assert.That(Describe(rejected)).IsEqualTo("Text¶¶");
    }

    [Test]
    public async Task ATable_InATableCell_IsAsWideAsTheCell()
    {
        var docx = Build("<w:tbl><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl><w:p/>" + section);

        var result = DocumentEditor.InsertTable(docx, 0, 0, 1, 2, Plain);

        await Assert.That(Body(result.Document)).Contains("<w:tblGrid><w:gridCol w:w=\"1887\" /><w:gridCol w:w=\"1887\" /></w:tblGrid>");
    }

    // Pictures

    static NewPicture Png(double width, double height) =>
        new([0x89, 0x50, 0x4E, 0x47], "image/png", width, height, "A square & a circle");

    [Test]
    public async Task APicture_GoesInTheLine_AtThePlace()
    {
        var docx = Build(P(R("Picture here")) + section);

        var result = DocumentEditor.InsertPicture(docx, 0, 7, Png(150, 75), Plain);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Picture][▣][ here] ¶");
        var body = Body(result.Document);
        await Assert.That(body).Contains("<w:r><w:rPr><w:noProof /></w:rPr><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\">");
        await Assert.That(body).Contains("<a:graphicFrameLocks noChangeAspect=\"1\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" />");
        await Assert.That(body).Contains("<a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:embed=\"rId1\" />");
        await Assert.That(body).Contains("<wp:extent cx=\"1905000\" cy=\"952500\" />");
        await Assert.That(body).Contains("<wp:docPr id=\"1\" name=\"Picture 1\" descr=\"A square &amp; a circle\" />");
        await Assert.That((result.Paragraph, result.Offset)).IsEqualTo((0, 8));
        await Assert.That(Read(result.Document, _ => _.MainDocumentPart!.ImageParts.Single().ContentType)).IsEqualTo("image/png");
        var outline = DocumentOutline.Read(result.Document);
        await Assert.That(outline.Paragraphs[0].Units.Select(_ => _.Kind)).IsEquivalentTo([EditUnitKind.Text, EditUnitKind.Drawing, EditUnitKind.Text]);
    }

    [Test]
    public async Task APicture_WiderThanTheText_IsMadeAsWideAsIt()
    {
        var docx = Build(P(R("x")) + section);

        var result = DocumentEditor.InsertPicture(docx, 0, 1, Png(902.6, 300), Plain);

        // 9026 twips is 451.3pt: half the width, and so half the height.
        await Assert.That(Body(result.Document)).Contains("<wp:extent cx=\"5731510\" cy=\"1905000\" />");
    }

    [Test]
    public async Task APicture_Tracked_IsAnInsertion()
    {
        var docx = Build(P(R("Picture here")) + section);

        var result = DocumentEditor.InsertPicture(docx, 0, 7, Png(10, 10), Tracked);

        await Assert.That(Shape(result.Document)).IsEqualTo("[Picture]{+[▣]+}[ here] ¶");
    }

    // Links

    static SourcePosition At(int run, int offset) =>
        new(run, offset);

    [Test]
    public async Task ALink_WrapsTheText_InTheHyperlinkStyle()
    {
        var docx = Build(P(R("Link here please")) + section);

        var edited = DocumentEditor.Link(docx, At(0, 0), At(0, 4), "https://example.com/", Plain);

        await Assert.That(Shape(edited)).IsEqualTo("<[Link]>[ here please] ¶");
        var body = Body(edited);
        await Assert.That(body).StartsWith("<w:p><w:hyperlink xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" w:history=\"1\" r:id=\"rId1\">");
        await Assert.That(body).Contains("<w:r><w:rPr><w:rStyle w:val=\"Hyperlink\" /></w:rPr><w:t xml:space=\"preserve\">Link</w:t></w:r></w:hyperlink>");
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.HyperlinkRelationships.Single().Uri.ToString())).IsEqualTo("https://example.com/");
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.StyleDefinitionsPart!.Styles!.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().Select(style => style.StyleId!.Value!).ToList())).Contains("Hyperlink");
    }

    [Test]
    public async Task ALink_ToABookmark_IsAnAnchor()
    {
        var docx = Build(P(R("See below")) + section);

        var edited = DocumentEditor.Link(docx, At(0, 4), At(0, 9), "#Results", Plain);

        await Assert.That(Body(edited)).Contains("<w:hyperlink w:history=\"1\" w:anchor=\"Results\">");
    }

    // Part of a link linked anew is taken out of the old one, which is left either side of it.
    [Test]
    public async Task PartOfALink_LinkedAnew_DividesTheOldOne()
    {
        var docx = Build(P(R("abcdef")) + section);
        var linked = DocumentEditor.Link(docx, At(0, 0), At(0, 6), "https://one.example/", Plain);

        var edited = DocumentEditor.Link(linked, At(0, 2), At(0, 4), "https://two.example/", Plain);

        await Assert.That(Shape(edited)).IsEqualTo("<[ab]><[cd]><[ef]> ¶");
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.HyperlinkRelationships.Select(link => link.Uri.Host).Order().ToList())).IsEquivalentTo(["one.example", "two.example"]);
    }

    [Test]
    public async Task ALink_AcrossParagraphs_IsRefused()
    {
        var docx = Build(P(R("one")) + P(R("two")) + section);

        await Assert.That(() => DocumentEditor.Link(docx, At(0, 1), At(1, 2), "https://example.com/", Plain)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ALinkRemoved_LeavesItsText_AndItsAddressGoes()
    {
        var docx = Build(P(R("Link here")) + section);
        var linked = DocumentEditor.Link(docx, At(0, 0), At(0, 4), "https://example.com/", Plain);

        // A caret in the link is enough to say which.
        var edited = DocumentEditor.Unlink(linked, At(0, 2), At(0, 2), Plain);

        await Assert.That(Shape(edited)).IsEqualTo("[Link][ here] ¶");
        await Assert.That(Read(edited, _ => _.MainDocumentPart!.HyperlinkRelationships.Count())).IsEqualTo(0);
    }

    // Replacing

    [Test]
    public async Task Replaced_TextIsFormattedAsTheFirstCharacterItReplaces()
    {
        var docx = Build(P(R("the cat "), R("sat", bold: true), R(" on the mat")) + section);

        var edited = DocumentEditor.Replace(docx, [new(0, 4, 7), new(0, 19, 22)], "dog", Plain);

        await Assert.That(Shape(edited)).IsEqualTo("[the dog ][b:sat][ on the dog] ¶");
    }

    [Test]
    public async Task Replaced_AcrossTwoRuns_TakesTheFirstsFormatting()
    {
        var docx = Build(P(R("Hello "), R("world", bold: true)) + section);

        var edited = DocumentEditor.Replace(docx, [new(0, 4, 8)], "p, W", Plain);

        await Assert.That(Shape(edited)).IsEqualTo("[Hellp, W][b:rld] ¶");
    }

    [Test]
    public async Task Replaced_Tracked_IsADeletionAndAnInsertion()
    {
        var docx = Build(P(R("the cat sat")) + section);

        var edited = DocumentEditor.Replace(docx, [new(0, 4, 7)], "dog", Tracked);

        await Assert.That(Describe(edited)).Contains("the {-cat-}{+dog+} sat");
    }

    [Test]
    public async Task Replaced_AcrossAField_IsRefused()
    {
        const string field =
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1 May</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var docx = Build(P(R("On "), field, R(" we met")) + section);

        await Assert.That(() => DocumentEditor.Replace(docx, [new(0, 2, 5)], "x", Plain)).Throws<InvalidOperationException>();
    }
}
