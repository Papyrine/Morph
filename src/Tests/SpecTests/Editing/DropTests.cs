using DocumentFormat.OpenXml.Wordprocessing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using static EditDocuments;
using static ReviewDocuments;

// Drag and drop (DocumentEditor.Drop), against what Word did with real mouse drags on the same text
// (WordProbe M-80 to M-85, C-22, M-62, I-15; src/word-behavior.md). Each fixture is the probe's own:
// `words`, `three-paragraphs`, `empty-paragraphs`, `table-2x2`, `image-inline`, `image-floating`.
public class DropTests
{
    // alpha 0-4, beta 6-9, gamma 13-17, "1,000" 42-46, the end 58.
    const string words = "alpha beta,  gamma-delta 3.14 don't (eps) 1,000 10:30 end.";

    const string picture =
        "<w:r><w:drawing><wp:inline><wp:extent cx=\"12700\" cy=\"12700\"/><wp:docPr id=\"1\" name=\"Picture 1\"/></wp:inline></w:drawing></w:r>";

    const string floating =
        "<w:r><w:drawing><wp:anchor simplePos=\"0\" relativeHeight=\"251658240\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">" +
        "<wp:simplePos x=\"0\" y=\"0\"/>" +
        "<wp:positionH relativeFrom=\"column\"><wp:posOffset>127000</wp:posOffset></wp:positionH>" +
        "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>254000</wp:posOffset></wp:positionV>" +
        "<wp:extent cx=\"914400\" cy=\"914400\"/><wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr id=\"1\" name=\"Picture 1\"/>" +
        "</wp:anchor></w:drawing></w:r>";

    static byte[] Words() =>
        Build(P(R(words)));

    static byte[] ThreeParagraphs() =>
        Build(P(R("First paragraph here.")) + P(R("Second "), R("bold ", bold: true), R("one.")) + P(R("Third and last.")));

    static byte[] EmptyParagraphs() =>
        Build(P(R("Above")) + P() + P() + P(R("Below")));

    static byte[] Table() =>
        Build(
            P(R("Before the table.")) +
            "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
            "<w:tr><w:tc>" + P(R("One")) + "</w:tc><w:tc>" + P(R("Two")) + "</w:tc></w:tr>" +
            "<w:tr><w:tc>" + P(R("Three")) + "</w:tc><w:tc>" + P(R("Four")) + "</w:tc></w:tr>" +
            "</w:tbl>" +
            P(R("After the table.")));

    static byte[] InlinePicture() =>
        Build(P(R("Text before "), picture, R(" after end.")) + P(R("Second paragraph.")));

    // The same, with a picture the editor put in, which the schema takes as a picture.
    static byte[] RealPicture() =>
        DocumentEditor.InsertPicture(
            Build(P(R("Text before  after end.")) + P(R("Second paragraph."))),
            0,
            12,
            new(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="), "image/png", 10, 10),
            Plain).Document;

    // The body's paragraphs, as they read: a picture as ▣, text a tracked change deleted left out.
    static string Text(byte[] docx) =>
        string.Join(
            " | ",
            DocumentOutline.Read(docx).Paragraphs
                .Where(_ => _.Part == ReviewPart.Body)
                .Select(
                    _ => string.Concat(
                        _.Units.Select(
                            unit => unit.Kind switch
                            {
                                EditUnitKind.Text => unit.Text,
                                EditUnitKind.Drawing => "▣",
                                _ => ""
                            }))));

    static string Selected(DropResult result) =>
        $"{result.StartParagraph}:{result.StartOffset}-{result.EndParagraph}:{result.EndOffset}";

    static DropResult Drag(byte[] docx, int from, int fromOffset, int to, int toOffset, int at, int atOffset, bool copy = false, EditOptions? options = null) =>
        DocumentEditor.Drop(docx, from, fromOffset, to, toOffset, at, atOffset, copy, options ?? Plain);

    // M-80, C-22: beta moved before "1,000" takes the space before it away and gets one after it.
    [Test]
    public async Task AWord_DroppedBeforeAnother_IsSpacedFromIt_AndLeavesNoSpaceBehind()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 42);

        await Assert.That(Text(result.Document)).IsEqualTo("alpha,  gamma-delta 3.14 don't (eps) beta 1,000 10:30 end.");
        await Assert.That(Selected(result)).IsEqualTo("0:37-0:42");
    }

    // M-80: dropped in "ga|mma" it goes to the nearer end of the word, its start.
    [Test]
    public async Task AWord_DroppedInsideAWord_GoesToItsNearerEnd()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 15);

        await Assert.That(Text(result.Document)).IsEqualTo("alpha,  beta gamma-delta 3.14 don't (eps) 1,000 10:30 end.");
        await Assert.That(Selected(result)).IsEqualTo("0:8-0:13");
    }

    // M-80: at the paragraph's end the space goes before it, after the full stop, and is not selected.
    [Test]
    public async Task AWord_DroppedAtTheEnd_IsSpacedFromTheFullStop()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 58);

        await Assert.That(Text(result.Document)).IsEqualTo("alpha,  gamma-delta 3.14 don't (eps) 1,000 10:30 end. beta");
        await Assert.That(Selected(result)).IsEqualTo("0:54-0:58");
    }

    [Test]
    public async Task AWord_DroppedAtTheStart_IsSpacedFromTheWordAfter()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 0);

        await Assert.That(Text(result.Document)).IsEqualTo("beta alpha,  gamma-delta 3.14 don't (eps) 1,000 10:30 end.");
        await Assert.That(Selected(result)).IsEqualTo("0:0-0:5");
    }

    // M-80 tracked: struck where it was, the space before it too, and inserted where it went.
    [Test]
    public async Task AWord_DroppedTracked_IsStruckAndInserted()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 42, options: Tracked);

        await Assert.That(Shape(result.Document)).IsEqualTo("[alpha]{-[ ]-}{-[beta]-}[,  gamma-delta 3.14 don't (eps) ]{+[beta]+}{+[ ]+}[1,000 10:30 end.] ¶");
        await Assert.That(Text(ReviewEditor.ResolveAll(result.Document, accept: false))).IsEqualTo(words);
        await Assert.That(Text(ReviewEditor.ResolveAll(result.Document, accept: true))).IsEqualTo("alpha,  gamma-delta 3.14 don't (eps) beta 1,000 10:30 end.");
    }

    // M-82, C-22: Ctrl+drag leaves the word where it was.
    [Test]
    public async Task AWord_Copied_StaysWhereItWas()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 42, copy: true);

        await Assert.That(Text(result.Document)).IsEqualTo("alpha beta,  gamma-delta 3.14 don't (eps) beta 1,000 10:30 end.");
        await Assert.That(Selected(result)).IsEqualTo("0:42-0:47");
    }

    [Test]
    public async Task AWord_CopiedTracked_IsOneInsertion()
    {
        var result = Drag(Words(), 0, 6, 0, 10, 0, 58, copy: true, options: Tracked);

        // The text copied is divided off into a run of its own where it stands, and is not struck.
        await Assert.That(Shape(result.Document)).IsEqualTo("[alpha ][beta][,  gamma-delta 3.14 don't (eps) 1,000 10:30 end.]{+[ ]+}{+[beta]+} ¶");
        await Assert.That(Selected(result)).IsEqualTo("0:59-0:63");
    }

    // M-83: dropped on itself or at either end, nothing changes, and the caret goes to the drop — in
    // "b|eta", the word's start.
    [Test]
    [Arguments(10, 10)]
    [Arguments(7, 6)]
    [Arguments(6, 6)]
    public async Task AWord_DroppedOnItself_ChangesNothing(int at, int caret)
    {
        var docx = Words();

        var result = Drag(docx, 0, 6, 0, 10, 0, at);

        await Assert.That(result.Document).IsSameReferenceAs(docx);
        await Assert.That(Selected(result)).IsEqualTo($"0:{caret}-0:{caret}");
    }

    // M-81: a paragraph moved with its mark goes, and its text joins the paragraph it is dropped in —
    // after "Third ", the nearer end of "Thir|d".
    [Test]
    public async Task AParagraph_MovedWithItsMark_JoinsTheParagraphDroppedIn()
    {
        var result = Drag(ThreeParagraphs(), 0, 0, 1, 0, 2, 4);

        await Assert.That(Text(result.Document)).IsEqualTo("Second bold one. | Third First paragraph here.and last.");
        await Assert.That(Selected(result)).IsEqualTo("1:6-1:27");
    }

    [Test]
    public async Task AParagraphsText_MovedWithoutItsMark_LeavesItEmpty()
    {
        var result = Drag(ThreeParagraphs(), 0, 0, 0, 21, 2, 4);

        await Assert.That(Text(result.Document)).IsEqualTo(" | Second bold one. | Third First paragraph here.and last.");
    }

    [Test]
    public async Task AParagraph_DroppedAtAnotherEnd_IsSpacedFromIt()
    {
        var result = Drag(ThreeParagraphs(), 0, 0, 1, 0, 2, 15);

        await Assert.That(Text(result.Document)).IsEqualTo("Second bold one. | Third and last. First paragraph here.");
    }

    [Test]
    public async Task PartOfAParagraph_DroppedAtTheNextsStart_JoinsIt()
    {
        var result = Drag(ThreeParagraphs(), 0, 6, 0, 21, 1, 0);

        await Assert.That(Text(result.Document)).IsEqualTo("First  | paragraph here.Second bold one. | Third and last.");
        await Assert.That(Selected(result)).IsEqualTo("1:0-1:15");
    }

    // Moved text keeps its own formatting.
    [Test]
    public async Task BoldText_StaysBold()
    {
        var result = Drag(ThreeParagraphs(), 1, 7, 1, 12, 0, 0);

        await Assert.That(Shape(result.Document)).StartsWith("[b:bold ][First paragraph here.] ¶");
    }

    // M-81 tracked: a whole paragraph's text is recorded as a move, as Word records it, and its mark is
    // struck.
    [Test]
    public async Task AParagraph_MovedTracked_IsAMove_AndItsMarkIsStruck()
    {
        var result = Drag(ThreeParagraphs(), 0, 0, 1, 0, 2, 4, options: Tracked);

        await Assert.That(Describe(result.Document)).IsEqualTo("{<First paragraph here.<}¶-Second bold one.¶Third {>First paragraph here.>}and last.¶");
        await Assert.That(DocumentReview.Read(result.Document).Changes.Select(_ => _.Kind)).Contains(ReviewChangeKind.Move);
        await Assert.That(Text(ReviewEditor.ResolveAll(result.Document, accept: false))).IsEqualTo("First paragraph here. | Second bold one. | Third and last.");
        await Assert.That(Text(ReviewEditor.ResolveAll(result.Document, accept: true))).IsEqualTo("Second bold one. | Third First paragraph here.and last.");
    }

    // M-85 tracked: the move, and the space smart paste puts before it an insertion of its own.
    [Test]
    public async Task AParagraph_MovedTracked_ToTheLastMark_SpacesItByAnInsertion()
    {
        var result = Drag(EmptyParagraphs(), 0, 0, 0, 5, 3, 5, options: Tracked);

        await Assert.That(Describe(result.Document)).IsEqualTo("{<Above<}¶¶¶Below{+ +}{>Above>}¶");
        await Assert.That(Selected(result)).IsEqualTo("3:6-3:11");
    }

    // Two paragraphs moved: the mark between them divides the paragraph they are dropped in.
    [Test]
    public async Task TwoParagraphs_Moved_DivideTheParagraphDroppedIn()
    {
        var result = Drag(ThreeParagraphs(), 0, 0, 2, 0, 2, 6);

        await Assert.That(Text(result.Document)).IsEqualTo("Third First paragraph here. | Second bold one.and last.");
        await Assert.That(Selected(result)).IsEqualTo("0:6-1:16");
    }

    // M-85: into an empty paragraph, which it fills, leaving its own empty; at a mark, before it.
    [Test]
    public async Task AWord_DroppedInAnEmptyParagraph_FillsIt()
    {
        var result = Drag(EmptyParagraphs(), 0, 0, 0, 5, 1, 0);

        await Assert.That(Text(result.Document)).IsEqualTo(" | Above |  | Below");
        await Assert.That(Selected(result)).IsEqualTo("1:0-1:5");
    }

    [Test]
    public async Task AWord_DroppedAtTheLastMark_IsSpacedFromTheWordBefore()
    {
        var result = Drag(EmptyParagraphs(), 0, 0, 0, 5, 3, 5);

        await Assert.That(Text(result.Document)).IsEqualTo(" |  |  | Below Above");
    }

    // M-84: text dropped into a cell, and a cell's text into another cell.
    [Test]
    public async Task Text_DroppedIntoACell_GoesInAsText()
    {
        var result = Drag(Table(), 0, 0, 0, 6, 2, 1);

        await Assert.That(Text(result.Document)).IsEqualTo("the table. | One | Before Two | Three | Four | After the table.");
        await Assert.That(Selected(result)).IsEqualTo("2:0-2:7");
    }

    [Test]
    public async Task ACellsText_DroppedIntoAnotherCell_LeavesItEmpty()
    {
        var result = Drag(Table(), 1, 0, 1, 3, 4, 1);

        await Assert.That(Text(result.Document)).IsEqualTo("Before the table. |  | Two | Three | One Four | After the table.");
    }

    // A selection from the body into a table is no run of paragraphs a drop can take.
    [Test]
    public async Task ASelection_IntoATable_IsRefused()
    {
        var docx = Table();

        var result = Drag(docx, 0, 7, 1, 2, 5, 0);

        await Assert.That(result.Document).IsSameReferenceAs(docx);
    }

    // M-62: a picture is moved to exactly where it is dropped, and nothing is spaced.
    [Test]
    public async Task APicture_DroppedInAWord_StaysWhereItIsDropped()
    {
        var result = Drag(InlinePicture(), 0, 12, 0, 13, 1, 3);

        await Assert.That(Text(result.Document)).IsEqualTo("Text before  after end. | Sec▣ond paragraph.");
        await Assert.That(Selected(result)).IsEqualTo("1:3-1:4");
    }

    [Test]
    public async Task APicture_Copied_IsADrawingOfItsOwn()
    {
        var result = Drag(InlinePicture(), 0, 12, 0, 13, 1, 3, copy: true);

        await Assert.That(Text(result.Document)).IsEqualTo("Text before ▣ after end. | Sec▣ond paragraph.");
        var ids = Read(result.Document, _ => _.MainDocumentPart!.Document!.Descendants<Wp.DocProperties>().Select(properties => properties.Id!.Value).ToList());
        await Assert.That(ids.Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task APicture_MovedTracked_IsStruckAndInserted()
    {
        var result = Drag(InlinePicture(), 0, 12, 0, 13, 0, 0, options: Tracked);

        await Assert.That(Shape(result.Document)).StartsWith("{+[▣]+}[Text before ]{-[▣]-}[ after end.] ¶");
    }

    // Every kind of drop writes a file the Open XML schema takes.
    [Test]
    public async Task EveryDrop_IsValid()
    {
        var drops = new[]
        {
            Drag(Words(), 0, 6, 0, 10, 0, 42),
            Drag(Words(), 0, 6, 0, 10, 0, 42, options: Tracked),
            Drag(Words(), 0, 6, 0, 10, 0, 58, copy: true, options: Tracked),
            Drag(ThreeParagraphs(), 0, 0, 1, 0, 2, 4),
            Drag(ThreeParagraphs(), 0, 0, 1, 0, 2, 4, options: Tracked),
            Drag(ThreeParagraphs(), 0, 0, 2, 0, 2, 6, options: Tracked),
            Drag(EmptyParagraphs(), 0, 0, 0, 5, 3, 5, options: Tracked),
            Drag(Table(), 1, 0, 1, 3, 4, 1, options: Tracked),
            Drag(RealPicture(), 0, 12, 0, 13, 1, 3, options: Tracked),
            Drag(RealPicture(), 0, 12, 0, 13, 1, 3, copy: true)
        };

        foreach (var drop in drops)
        {
            var errors = Read(drop.Document, _ => new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(_).Select(error => $"{error.Path?.XPath}: {error.Description}").ToList());
            await Assert.That(errors).IsEmpty();
        }
    }

    // I-15: Ctrl+drag of a floating picture duplicates it, anchored with it, moved by the drag.
    [Test]
    public async Task AFloatingPicture_Duplicated_IsMovedCopy()
    {
        var docx = Build(P(R("First paragraph."), floating));

        var edited = DocumentEditor.DuplicatePicture(docx, 0, 16, 30, 40, Plain);

        var anchors = Read(
            edited,
            _ => _.MainDocumentPart!.Document!.Descendants<Wp.Anchor>()
                .Select(anchor => $"{anchor.GetFirstChild<Wp.DocProperties>()!.Id!.Value}@{anchor.HorizontalPosition!.PositionOffset!.Text},{anchor.VerticalPosition!.PositionOffset!.Text}")
                .ToList());
        await Assert.That(string.Join(' ', anchors)).IsEqualTo("2@508000,762000 1@127000,254000");
    }
}
