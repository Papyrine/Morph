using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlFootnote = DocumentFormat.OpenXml.Wordprocessing.Footnote;
using OoxmlRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// What is not text, moved past another of its kind — a picture dragged over a field, two pictures
// swapped — in a rewritten paragraph (a drag in the editor's open paragraph). Untracked it is put down
// whole at its new place; tracked it is struck where it was and inserted where it went, so rejecting
// the change puts the paragraph back.
public class AtomMoveTests
{
    const string picture =
        "<w:r><w:drawing><wp:inline><wp:extent cx=\"12700\" cy=\"12700\"/><wp:docPr id=\"1\" name=\"Picture 1\"/></wp:inline></w:drawing></w:r>";

    const string secondPicture =
        "<w:r><w:drawing><wp:inline><wp:extent cx=\"25400\" cy=\"25400\"/><wp:docPr id=\"2\" name=\"Picture 2\"/></wp:inline></w:drawing></w:r>";

    const string field =
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        "<w:r><w:instrText xml:space=\"preserve\"> DATE </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        "<w:r><w:t>1 May</w:t></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    const string note =
        "<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r>";

    const string simpleField =
        "<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>4</w:t></w:r></w:fldSimple>";

    // Units: 0 "ab", 1 the picture, 2 "cd", 3 the field, 4 "ef".
    static byte[] PictureThenField() =>
        Build(P(R("ab"), picture, R("cd"), field, R("ef")));

    static string Kinds(byte[] docx) =>
        string.Join(' ', DocumentOutline.Read(docx).Paragraphs[0].Units.Select(_ => _.Kind));

    [Test]
    public async Task APictureMovedPastAField_IsPutDownWhole_AtItsNewPlace()
    {
        var docx = PictureThenField();

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "ab"), K(3), T(2, "cd"), K(1), T(4, "ef")));

        await Assert.That(Kinds(edited)).IsEqualTo("Text Field Text Drawing Text");
        await Assert.That(Spelt(edited)).IsEqualTo("ab{F}cd▣ef");
        await Assert.That(Xml(edited).Split("<w:drawing>").Length - 1).IsEqualTo(1);
        await Assert.That(Xml(edited)).DoesNotContain("﷐");
    }

    // Text dragged along with the picture moves with it: the moved picture's mark sits among the
    // characters the diff places, wherever they land.
    [Test]
    public async Task APictureMovedWithText_KeepsItsPlaceInThatText()
    {
        var docx = PictureThenField();

        var edited = Rewrite(docx, 0, Plain, Reads(T(0, "a"), T(2, "d"), K(3), T(4, "e"), T(0, "b"), K(1), T(2, "c"), T(4, "f")));

        await Assert.That(Spelt(edited)).IsEqualTo("ad{F}eb▣cf");
    }

    [Test]
    public async Task APictureMovedPastAField_Tracked_IsStruckAndInserted_AndRejectingPutsItBack()
    {
        var docx = PictureThenField();

        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "ab"), K(3), T(2, "cd"), K(1), T(4, "ef")));

        var shape = Shape(edited);
        await Assert.That(shape).Contains("{-[▣]-}");
        await Assert.That(shape).Contains("{+[▣]+}");
        await Assert.That(shape.IndexOf("{-[▣]-}", StringComparison.Ordinal)).IsLessThan(shape.IndexOf("{+[▣]+}", StringComparison.Ordinal));

        // Both pictures stand in the file until the change is settled, each with an id of its own.
        var ids = Read(edited, _ => _.MainDocumentPart!.Document!.Descendants<Wp.DocProperties>().Select(properties => properties.Id!.Value).ToList());
        await Assert.That(ids.Distinct().Count()).IsEqualTo(2);

        // Rejected, the paragraph's runs are its own again (the diff leaves a w:t it divided marked to
        // keep its spaces).
        await Assert.That(Shape(ReviewEditor.ResolveAll(edited, accept: false))).IsEqualTo(Shape(docx));
        var accepted = ReviewEditor.ResolveAll(edited, accept: true);
        await Assert.That(Kinds(accepted)).IsEqualTo("Text Field Text Drawing Text");
    }

    // Two pictures swapped either side of text: the longest run in order stays, the other moves.
    [Test]
    public async Task TwoPicturesSwapped_MoveOneOfThem()
    {
        var docx = Build(P(picture, R("x"), secondPicture));

        var edited = Rewrite(docx, 0, Plain, Reads(K(2), T(1, "x"), K(0)));

        var names = Read(edited, _ => _.MainDocumentPart!.Document!.Descendants<Wp.DocProperties>().Select(properties => properties.Name!.Value).ToList());
        await Assert.That(string.Join(',', names)).IsEqualTo("Picture 2,Picture 1");
        await Assert.That(Kinds(edited)).IsEqualTo("Drawing Text Drawing");
    }

    // A note's reference moved keeps its note. Tracked it cannot be moved, since both places would
    // refer to the one note: dragged past a picture, the picture moves past it instead, and two notes
    // cannot pass each other.
    [Test]
    public async Task ANotesReferenceMoved_KeepsItsNote_AndTrackedThePictureMovesInstead()
    {
        var docx = WithFootnote(Build(P(R("Claim"), note, R(" made"), picture)));

        var plain = Rewrite(docx, 0, Plain, Reads(T(0, "Claim"), K(3), T(2, " made"), K(1)));

        await Assert.That(Notes(plain)).IsEqualTo(1);
        await Assert.That(Kinds(plain)).IsEqualTo("Text Drawing Text Note");

        var tracked = Rewrite(docx, 0, Tracked, Reads(T(0, "Claim"), K(3), T(2, " made"), K(1)));
        await Assert.That(Notes(tracked)).IsEqualTo(1);
        await Assert.That(Shape(tracked)).Contains("{+[▣]+}");
        await Assert.That(Xml(tracked).Split("footnoteReference").Length - 1).IsEqualTo(1);

        var notes = WithFootnote(Build(P(R("a"), note, R("b"), note.Replace("w:id=\"1\"", "w:id=\"2\""))));
        await Assert.That(() => Rewrite(notes, 0, Tracked, Reads(T(0, "a"), K(3), T(2, "b"), K(1)))).Throws<InvalidOperationException>();
    }

    // A simple field moved under tracking goes in as the complex field it stands for, since only runs
    // can be inserted.
    [Test]
    public async Task ASimpleFieldMoved_Tracked_GoesInAsAComplexField()
    {
        var docx = Build(P(R("ab"), simpleField, picture, R("cd")));

        // Units: 0 "ab", 1 the simple field, 2 the picture, 3 "cd".
        var edited = Rewrite(docx, 0, Tracked, Reads(T(0, "ab"), K(2), K(1), T(3, "cd")));

        await Assert.That(Xml(edited)).Contains("w:instrText xml:space=\"preserve\"> PAGE </w:instrText>");
        var rejected = ReviewEditor.ResolveAll(edited, accept: false);
        await Assert.That(Shape(rejected)).IsEqualTo(Shape(docx));
        await Assert.That(Xml(rejected)).Contains("<w:fldSimple w:instr=\" PAGE \">");
        await Assert.That(Xml(rejected)).DoesNotContain("instrText");
    }

    // The paragraph's text, with a picture as ▣ and a field as {F}.
    static string Spelt(byte[] docx) =>
        string.Concat(
            DocumentOutline.Read(docx).Paragraphs[0].Units.Select(
                _ => _.Kind switch
                {
                    EditUnitKind.Drawing => "▣",
                    EditUnitKind.Field => "{F}",
                    _ => _.Text
                }));

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
            part.Footnotes =
            [
                with(
                    new OoxmlFootnote(new Paragraph(new OoxmlRun(new Text("separator"))))
                    {
                        Id = -1
                    },
                    new OoxmlFootnote(new Paragraph(new OoxmlRun(new Text("The note."))))
                    {
                        Id = 1
                    })
            ];
        }

        return stream.ToArray();
    }
}
