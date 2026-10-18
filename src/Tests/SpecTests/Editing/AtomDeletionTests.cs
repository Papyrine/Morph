using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlFootnote = DocumentFormat.OpenXml.Wordprocessing.Footnote;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using static EditDocuments;
using static ReviewDocuments;

// What is not text, deleted: a picture, a page break, a field, a note's reference — left out of a
// rewritten paragraph (Backspace beside it in the editor), or inside text deleted across the pages.
public class AtomDeletionTests
{
    const string picture =
        "<w:r><w:drawing><wp:inline><wp:extent cx=\"12700\" cy=\"12700\"/><wp:docPr id=\"1\" name=\"Picture 1\"/></wp:inline></w:drawing></w:r>";

    const string field =
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:t>1 May</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    [Test]
    public async Task APictureLeftOut_IsDeleted()
    {
        var docx = Build(P(R("ab"), picture, R("cd")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "ab"), T(2, "cd")));

        await Assert.That(Shape(edited)).IsEqualTo("[ab][cd] ¶");
    }

    [Test]
    public async Task APictureLeftOut_Tracked_IsADeletion_ThatRejectingPutsBack()
    {
        var docx = Build(P(R("ab"), picture, R("cd")));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "ab"), T(2, "cd")));

        await Assert.That(Shape(edited)).IsEqualTo("[ab]{-[▣]-}[cd] ¶");
        await Assert.That(DocumentReview.Read(edited).Changes.Select(_ => _.Kind)).IsEquivalentTo([ReviewChangeKind.Deletion]);
        await Assert.That(Xml(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Xml(docx));
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: true))).IsEqualTo("[ab][cd] ¶");
    }

    // A picture in a run with text: only the picture goes, the run's text either side stays.
    [Test]
    public async Task APictureInARunWithText_GoesAlone()
    {
        var docx = Build(P("<w:r><w:t>ab</w:t><w:drawing><wp:inline><wp:extent cx=\"12700\" cy=\"12700\"/><wp:docPr id=\"1\" name=\"P\"/></wp:inline></w:drawing><w:t>cd</w:t></w:r>"));
        var units = DocumentOutline.Read(docx).Paragraphs[0].Units;

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, units[0].Text), T(2, units[2].Text)));

        await Assert.That(Shape(edited)).IsEqualTo("[ab][cd] ¶");
    }

    // A page break in a paragraph of its own: the paragraph is left empty, to be joined to its
    // neighbour by a Backspace more.
    [Test]
    public async Task APageBreakLeftOut_LeavesItsParagraphEmpty()
    {
        var docx = Build(P(R("before")) + "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>" + P(R("after")));

        var edited = Rewrite(docx, 1, Plain, Reads());

        await Assert.That(Xml(edited)).Contains("</w:p><w:p /><w:p>");
    }

    [Test]
    public async Task AFieldLeftOut_IsDeletedFromItsBeginningToItsEnd()
    {
        var docx = Build(P(R("On "), field, R(" we met")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "On "), T(2, " we met")));

        await Assert.That(Shape(edited)).IsEqualTo("[On ][ we met] ¶");
    }

    [Test]
    public async Task AFieldLeftOut_Tracked_HasItsCodeStruckOutToo()
    {
        var docx = Build(P(R("On "), field, R(" we met")));

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "On "), T(2, " we met")));

        var xml = Xml(edited);
        await Assert.That(xml).Contains("<w:delInstrText xml:space=\"preserve\"> DATE </w:delInstrText>");
        await Assert.That(xml).Contains("<w:delText xml:space=\"preserve\">1 May</w:delText>");

        // Rejected, the field is back as it was, its text written with its spaces kept.
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Shape(docx));
    }

    [Test]
    public async Task ASimpleFieldLeftOut_IsDeleted()
    {
        var docx = Build(P(R("Page "), "<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple>", R(" of it")));

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "Page "), T(2, " of it")));

        await Assert.That(Xml(edited)).DoesNotContain("fldSimple");
    }

    // Text a tracked change deleted is settled in the review pane, not deleted again.
    [Test]
    public async Task TextATrackedChangeDeleted_CannotBeLeftOut()
    {
        var docx = Build(P(R("ab"), Del("Bo", 1, "gone"), R("cd")));

        await Assert.That(() => Rewrite(docx, 0, Plain, Reads(T(0, "ab"), T(2, "cd")))).Throws<InvalidOperationException>();
    }

    // A field that began in the paragraph before ends in this one: half of it cannot go.
    [Test]
    public async Task AFieldBegunInAnEarlierParagraph_CannotBeLeftOut()
    {
        var docx = Build(
            P(R("x"), "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> TOC </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>") +
            P("<w:r><w:t>entry</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>", R(" after")));
        var units = DocumentOutline.Read(docx).Paragraphs[1].Units;

        await Assert.That(units[0].Kind).IsEqualTo(EditUnitKind.Field);
        await Assert.That(() => Rewrite(docx, 1, Plain, Reads(T(1, units[1].Text)))).Throws<InvalidOperationException>();
    }

    // A note's reference takes its note with it, as Word's does; tracked, the note stays until the
    // deletion is accepted.
    [Test]
    public async Task ANotesReferenceLeftOut_TakesTheNoteWithIt()
    {
        var docx = WithFootnote(Build(P(R("Claim"), "<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r>", R(" made"))));

        var plain = Rewrite(docx, 0, Plain, Reads(T(0, "Claim"), T(2, " made")));
        var tracked = Rewrite(docx, 0, Tracked, Reads(T(0, "Claim"), T(2, " made")));

        await Assert.That(Notes(plain)).IsEqualTo(0);
        await Assert.That(Shape(plain)).IsEqualTo("[Claim][ made] ¶");
        await Assert.That(Notes(tracked)).IsEqualTo(1);
    }

    static int Notes(byte[] docx) =>
        Read(docx, _ => _.MainDocumentPart!.FootnotesPart!.Footnotes!.Elements<OoxmlFootnote>().Count(note => note.Id?.Value > 0));

    static byte[] WithFootnote(byte[] docx)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        stream.Position = 0;
        using (var package = WordprocessingDocument.Open(stream, true))
        {
            var part = package.MainDocumentPart!.AddNewPart<FootnotesPart>();
            part.Footnotes = new(
                new OoxmlFootnote(new Paragraph(new OoxmlRun(new Text("separator")))) {Id = -1},
                new OoxmlFootnote(new Paragraph(new OoxmlRun(new Text("The note.")))) {Id = 1});
        }

        return stream.ToArray();
    }

    // Deleted across the pages, a selection takes in what is not text as well as the text.
    [Test]
    public async Task TextDeleted_TakesThePictureInItWith()
    {
        var docx = Build(P(R("ab"), picture, R("cd")));

        // From "b" to "c": runs 0 and 2 (the picture's run is run 1).
        var edited = DocumentEditor.Delete(docx, new(0, 1), new(2, 1), Plain).Document;

        await Assert.That(Shape(edited)).IsEqualTo("[a][d] ¶");
    }

    [Test]
    public async Task TextDeleted_UpToAPicture_LeavesIt()
    {
        var docx = Build(P(R("ab"), picture, R("cd")));

        var edited = DocumentEditor.Delete(docx, new(0, 0), new(0, 2), Plain).Document;

        await Assert.That(Shape(edited)).IsEqualTo("[▣][cd] ¶");
    }
}
