using static EditDocuments;
using static ReviewDocuments;

// A picture in the text made larger or smaller: its size written as the drawing's extent and the
// picture's own, in EMUs (Word-probed 2026-10-19, see DocumentEditor.Pictures.cs).
public class PictureResizeTests
{
    const string section = "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>";

    static readonly byte[] png = [0x89, 0x50, 0x4E, 0x47];

    // "Picture here" with a 150pt by 75pt picture after "Picture", at place 7.
    static byte[] Pictured() =>
        DocumentEditor.InsertPicture(Build(P(R("Picture here")) + section), 0, 7, new(png, "image/png", 150, 75), Plain).Document;

    [Test]
    public async Task Resized_IsTheDrawingsExtent_AndThePicturesOwn()
    {
        var edited = DocumentEditor.ResizePicture(Pictured(), 0, 7, 300, 150, Plain);

        var body = Xml(edited);
        await Assert.That(body).Contains("<wp:extent cx=\"3810000\" cy=\"1905000\" />");
        await Assert.That(body).Contains("<a:ext cx=\"3810000\" cy=\"1905000\" />");
        await Assert.That(Shape(edited)).IsEqualTo("[Picture][▣][ here] ¶");
    }

    // Free of its aspect, a picture is as wide and as high as it is made.
    [Test]
    public async Task Resized_OutOfProportion_KeepsBothSizes()
    {
        var edited = DocumentEditor.ResizePicture(Pictured(), 0, 7, 300, 30, Plain);

        await Assert.That(Xml(edited)).Contains("<wp:extent cx=\"3810000\" cy=\"381000\" />");
    }

    [Test]
    public async Task WhereThereIsNoPicture_SaysSo() =>
        await Assert.That(() => DocumentEditor.ResizePicture(Pictured(), 0, 3, 10, 10, Plain)).Throws<InvalidOperationException>();

    [Test]
    public async Task ThePicturesData_IsItsImagePart()
    {
        var data = DocumentEditor.PictureData(Pictured(), 0, 7);

        await Assert.That(data?.Data).IsEquivalentTo(png);
        await Assert.That(data?.ContentType).IsEqualTo("image/png");
    }
}
