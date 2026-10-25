using static EditDocuments;
using static ReviewDocuments;

// Layout > Wrap Text and sizing a floating picture, against what Word wrote (Word-probed 2026-10-23,
// see DocumentEditor.SetPictureWrap), and the parser's note of where a floating picture is, which
// the viewer finds it by.
public class PictureWrapTests
{
    const string section =
        "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>";

    // A real one-pixel PNG, so the parser reads it as a picture.
    static readonly byte[] pixel = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // "Before " and a 100pt by 50pt picture after it, in the line: the picture is place 7.
    // The body as text, without the drawing namespace the SDK declares where it is first used.
    static string Body(byte[] docx) =>
        Xml(docx).Replace(" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"", "");

    static byte[] Pictured() =>
        DocumentEditor.InsertPicture(Build(P(R("Before after")) + section), 0, 7, new(pixel, "image/png", 100, 50), Plain).Document;

    [Test]
    public async Task Square_MakesThePictureFloat_AsWordWritesIt()
    {
        var edited = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        var xml = Body(edited);
        await Assert.That(xml).Contains("<wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"251658240\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">");
        await Assert.That(xml).Contains("<wp:simplePos x=\"0\" y=\"0\" /><wp:positionH relativeFrom=\"column\"><wp:posOffset>0</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV><wp:extent cx=\"1270000\" cy=\"635000\" />");
        await Assert.That(xml).Contains("<wp:wrapSquare wrapText=\"bothSides\" /><wp:docPr ");
        await Assert.That(xml).DoesNotContain("wp:inline");
        await Assert.That(DocumentEditor.PictureWrapOf(edited, 0, 7)).IsEqualTo(PictureWrap.Square);
    }

    [Test]
    [Arguments("Tight", "<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\">", "behindDoc=\"1\"")]
    [Arguments("TopAndBottom", "<wp:wrapTopAndBottom />", "behindDoc=\"0\"")]
    [Arguments("BehindText", "<wp:wrapNone />", "behindDoc=\"1\"")]
    [Arguments("InFrontOfText", "<wp:wrapNone />", "behindDoc=\"0\"")]
    public async Task EachWrap_IsWordsElement(string name, string element, string behind)
    {
        var wrap = Enum.Parse<PictureWrap>(name);
        var floating = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        var edited = DocumentEditor.SetPictureWrap(floating, 0, 7, wrap, Plain);

        var xml = Body(edited);
        await Assert.That(xml).Contains(element);
        await Assert.That(xml).Contains(behind);
        await Assert.That(xml.Split("<wp:wrap").Length).IsEqualTo(wrap == PictureWrap.Tight ? 3 : 2);
        await Assert.That(DocumentEditor.PictureWrapOf(edited, 0, 7)).IsEqualTo(wrap);
    }

    [Test]
    public async Task InLine_PutsThePictureBackInTheLine()
    {
        var floating = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        var inline = DocumentEditor.SetPictureWrap(floating, 0, 7, PictureWrap.Inline, Plain);

        var xml = Body(inline);
        await Assert.That(xml).Contains("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"1270000\" cy=\"635000\" />");
        await Assert.That(xml).DoesNotContain("wp:anchor");
        await Assert.That(DocumentEditor.PictureWrapOf(inline, 0, 7)).IsEqualTo(PictureWrap.Inline);
    }

    [Test]
    public async Task AFloatingPicture_IsSized()
    {
        var floating = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        var sized = DocumentEditor.ResizePicture(floating, 0, 7, 200, 100, Plain);

        await Assert.That(Xml(sized)).Contains("<wp:extent cx=\"2540000\" cy=\"1270000\" />");
        await Assert.That(Xml(sized)).Contains("<a:ext cx=\"2540000\" cy=\"1270000\" />");
    }

    // An arrow nudges a floating picture 0.75pt, untracked (WordProbe I-12).
    [Test]
    [Property("WordBehavior", "R-I-6")]
    public async Task AFloatingPicture_IsNudged()
    {
        var floating = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        var nudged = DocumentEditor.NudgePicture(floating, 0, 7, 0.75, -1.5, Tracked);

        var xml = Body(nudged);
        await Assert.That(xml).Contains("<wp:positionH relativeFrom=\"column\"><wp:posOffset>9525</wp:posOffset></wp:positionH>");
        await Assert.That(xml).Contains("<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>-19050</wp:posOffset></wp:positionV>");
        await Assert.That(xml).DoesNotContain("w:ins");
    }

    // Delete with a floating picture selected removes it; tracked, it is struck (WordProbe I-13).
    [Test]
    [Property("WordBehavior", "R-I-7")]
    public async Task AFloatingPicture_IsDeleted()
    {
        var floating = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        var plain = DocumentEditor.DeletePicture(floating, 0, 7, Plain);
        var tracked = DocumentEditor.DeletePicture(floating, 0, 7, Tracked);

        await Assert.That(Body(plain)).DoesNotContain("w:drawing");
        await Assert.That(Body(plain)).Contains("Before ");
        await Assert.That(Body(tracked)).Contains("<w:del ");
        await Assert.That(Body(tracked)).Contains("w:drawing");
    }

    // The parser notes where a floating picture's drawing is, in the coordinates the editor's own
    // outline counts places by, so the place it names is the picture's.
    [Test]
    public async Task AFloatingPicture_IsStampedWithItsPlace()
    {
        var floating = DocumentEditor.SetPictureWrap(Pictured(), 0, 7, PictureWrap.Square, Plain);

        using var stream = new MemoryStream(floating);
        var parsed = DocumentConverter.Parse(stream, null, captureSources: true);
        var image = parsed.Elements.OfType<FloatingImageElement>().Single();

        await Assert.That(image.Source).IsNotNull();
        var source = image.Source!.Value;
        var outline = DocumentOutline.Read(floating);
        var paragraph = outline.ParagraphOf(source.Run)!;
        await Assert.That(paragraph.Ordinal).IsEqualTo(0);
        await Assert.That(paragraph.OffsetOf(new(source.Run, source.Start))).IsEqualTo(7);
    }
}
