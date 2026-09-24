// The viewer lays a document out once and paints pages one at a time, at whatever resolution the zoom asks
// for. Every page must come out exactly as the one-shot converter paints it, whichever order and
// resolutions the pages are asked for in.
public class PagedDocumentTests
{
    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task RenderPage_IsByteIdenticalToTheConverter(InputFormat source)
    {
        var expected = ConversionService.RenderPngPages(
            Sample.BytesFor(source),
            source,
            new()
            {
                Dpi = 96
            },
            Sample.FontDirectory);

        using var document = PagedDocument.Open(Sample.BytesFor(source), source, Sample.FontDirectory);

        await Assert.That(document.PageCount).IsEqualTo(expected.Count);
        for (var index = 0; index < document.PageCount; index++)
        {
            await Assert.That(document.RenderPage(index, 96).AsSpan().SequenceEqual(expected[index])).IsTrue();
        }
    }

    // The painter truncates the page's pixel size, as every Morph raster does.
    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task RenderPage_IsThePageSizeAtTheDpi(InputFormat source)
    {
        using var document = PagedDocument.Open(Sample.BytesFor(source), source, Sample.FontDirectory);

        var png = document.RenderPage(0, 150);

        await Assert.That(Width(png)).IsEqualTo((int) (document.WidthPoints(0) * 150 / 72.0));
        await Assert.That(Height(png)).IsEqualTo((int) (document.HeightPoints(0) * 150 / 72.0));
    }

    // Zooming and the thumbnail sidebar interleave resolutions; a context reused across pages and
    // resolutions (with its pictures released between pages) must not change a single pixel.
    [Test]
    public async Task InterleavedResolutions_RenderIdentically()
    {
        using var reference = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
        var fresh = new Dictionary<(int Page, int Dpi), byte[]>();
        foreach (var dpi in new[] { 96, 150, 36 })
        {
            for (var page = 0; page < reference.PageCount; page++)
            {
                using var single = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
                fresh[(page, dpi)] = single.RenderPage(page, dpi);
            }
        }

        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
        foreach (var (page, dpi) in new[] { (1, 150), (0, 36), (0, 96), (1, 36), (0, 150), (1, 96), (0, 96) })
        {
            await Assert.That(document.RenderPage(page, dpi).AsSpan().SequenceEqual(fresh[(page, dpi)])).IsTrue();
        }
    }

    [Test]
    public async Task ConcurrentRenders_AreSerialisedAndIdentical()
    {
        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
        var expected = document.RenderPage(0, 72);

        var renders = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => document.RenderPage(0, 72))));

        foreach (var render in renders)
        {
            await Assert.That(render.AsSpan().SequenceEqual(expected)).IsTrue();
        }
    }

    [Test]
    public async Task TextLayer_IsCachedPerPage()
    {
        using var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);

        await Assert.That(document.TextLayer(0)).IsSameReferenceAs(document.TextLayer(0));
        await Assert.That(document.TextLayer(0).Text).Contains("MEETING");
    }

    [Test]
    public async Task RenderPage_AfterDispose_Throws()
    {
        var document = PagedDocument.Open(Sample.DocxBytes, InputFormat.Docx, Sample.FontDirectory);
        document.Dispose();

        await Assert.That(() => document.RenderPage(0, 96)).Throws<ObjectDisposedException>();
    }

    // PNG IHDR: width then height, big-endian, at byte offsets 16 and 20.
    static int Width(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    static int Height(byte[] png) =>
        (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
}
