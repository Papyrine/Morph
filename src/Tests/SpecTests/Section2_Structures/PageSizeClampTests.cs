using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// A page dimension (<c>w:pgSz w:w</c>/<c>@w:h</c>) is an unbounded twips measure in the file. Left raw
/// it scales straight into each backend's raster surface (an SKBitmap's width is
/// <c>WidthPoints x DPI/72</c> at 4 bytes/px), so a crafted value could allocate — or integer-overflow —
/// a bitmap of billions of pixels and exhaust memory (OOM). <c>DocumentParser</c> clamps each dimension
/// to a ceiling far above any real paper (A0 and ANSI E are ~3370pt; Word's own UI caps at 22in =
/// 1584pt), which leaves every legitimate document untouched.
/// </summary>
public class PageSizeClampTests
{
    // Mirrors DocumentParser.maxPageDimensionPoints.
    const double maxPageDimensionPoints = 14400;

    [Test]
    public async Task DocumentParser_ClampsHugePageSize()
    {
        // ~1e8 points per side before the clamp.
        using var stream = BuildPageSizeDocument(2_000_000_000u, 2_000_000_000u);
        var document = new DocumentParser().Parse(stream);

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(maxPageDimensionPoints);
        await Assert.That(document.PageSettings.HeightPoints).IsEqualTo(maxPageDimensionPoints);
    }

    [Test]
    public async Task DocumentParser_LeavesLetterPageSizeUntouched()
    {
        // Letter: 12240 x 15840 twips = 612 x 792 pt.
        using var stream = BuildPageSizeDocument(12240u, 15840u);
        var document = new DocumentParser().Parse(stream);

        await Assert.That(document.PageSettings.WidthPoints).IsEqualTo(612d);
        await Assert.That(document.PageSettings.HeightPoints).IsEqualTo(792d);
    }

    static MemoryStream BuildPageSizeDocument(uint widthTwips, uint heightTwips)
    {
        var body = new Body(
            new Paragraph(new W.Run(new Text("x"))),
            new SectionProperties(new PageSize { Width = widthTwips, Height = heightTwips }));

        var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = [with(body)];
        }

        stream.Position = 0;
        return stream;
    }
}
