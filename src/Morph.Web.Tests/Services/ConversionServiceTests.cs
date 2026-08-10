public class ConversionServiceTests
{
    static readonly byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Test]
    public async Task WritableFormats_CoverAllFive()
    {
        var formats = ConversionService.WritableFormats.Select(_ => _.Format).ToList();

        await Assert.That(formats).Contains(OutputFormat.Png);
        await Assert.That(formats).Contains(OutputFormat.Pdf);
        await Assert.That(formats).Contains(OutputFormat.Html);
        await Assert.That(formats).Contains(OutputFormat.Markdown);
        await Assert.That(formats).Contains(OutputFormat.Text);
    }

    [Test]
    public async Task ReadableFormats_CoverTheThreeOfficeInputs()
    {
        var formats = ConversionService.ReadableFormats.Select(_ => _.Format).ToList();

        await Assert.That(formats).Contains(InputFormat.Docx);
        await Assert.That(formats).Contains(InputFormat.Xlsx);
        await Assert.That(formats).Contains(InputFormat.Pptx);
        await Assert.That(ConversionService.ReadableAccept).IsEqualTo(".docx,.xlsx,.pptx");
    }

    [Test]
    public async Task Detect_MapsExtensionToFormat()
    {
        await Assert.That(ConversionService.Detect("report.docx")?.Format).IsEqualTo(InputFormat.Docx);
        await Assert.That(ConversionService.Detect("BUDGET.XLSX")?.Format).IsEqualTo(InputFormat.Xlsx);
        await Assert.That(ConversionService.Detect("deck.pptx")?.Format).IsEqualTo(InputFormat.Pptx);
        await Assert.That(ConversionService.Detect("notes.txt")).IsNull();
        // The legacy binary formats share a prefix with the OOXML ones but aren't readable.
        await Assert.That(ConversionService.Detect("legacy.doc")).IsNull();
        await Assert.That(ConversionService.Detect("legacy.xls")).IsNull();
        await Assert.That(ConversionService.Detect("legacy.ppt")).IsNull();
    }

    [Test]
    public async Task CanRead_OfficeOoxmlOnly()
    {
        await Assert.That(ConversionService.CanRead("report.docx")).IsTrue();
        await Assert.That(ConversionService.CanRead("REPORT.DOCX")).IsTrue();
        await Assert.That(ConversionService.CanRead("budget.xlsx")).IsTrue();
        await Assert.That(ConversionService.CanRead("deck.pptx")).IsTrue();
        await Assert.That(ConversionService.CanRead("notes.txt")).IsFalse();
        await Assert.That(ConversionService.CanRead("image.png")).IsFalse();
    }

    // A deck renders one page per slide and a workbook one per printed page, so the noun differs; the
    // upload panel reads it off here.
    [Test]
    public async Task PageLabel_PluralisesPerSource()
    {
        await Assert.That(ConversionService.Find(InputFormat.Docx).PageLabel(1)).IsEqualTo("1 page");
        await Assert.That(ConversionService.Find(InputFormat.Xlsx).PageLabel(3)).IsEqualTo("3 pages");
        await Assert.That(ConversionService.Find(InputFormat.Pptx).PageLabel(1)).IsEqualTo("1 slide");
        await Assert.That(ConversionService.Find(InputFormat.Pptx).PageLabel(2)).IsEqualTo("2 slides");
    }

    [Test]
    public async Task SampleAsset_PointsAtTheBundledFile()
    {
        await Assert.That(ConversionService.Find(InputFormat.Docx).SampleAsset).IsEqualTo("sample/sample.docx");
        await Assert.That(ConversionService.Find(InputFormat.Xlsx).SampleAsset).IsEqualTo("sample/sample.xlsx");
        await Assert.That(ConversionService.Find(InputFormat.Pptx).SampleAsset).IsEqualTo("sample/sample.pptx");
    }

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task RenderPngPages_ProducesAPngPerPage(InputFormat source)
    {
        var pages = ConversionService.RenderPngPages(
            Sample.BytesFor(source),
            source,
            new()
            {
                Dpi = 96
            },
            Sample.FontDirectory);

        await Assert.That(pages.Count).IsGreaterThanOrEqualTo(1);
        foreach (var page in pages)
        {
            await Assert.That(page[..8]).IsEquivalentTo(pngSignature);
        }
    }

    [Test]
    public async Task RenderPngPages_HigherDpi_ProducesWiderImage()
    {
        var low = ConversionService.RenderPngPages(
            Sample.DocxBytes,
            InputFormat.Docx,
            new()
            {
                Dpi = 96
            },
            Sample.FontDirectory);
        var high = ConversionService.RenderPngPages(
            Sample.DocxBytes,
            InputFormat.Docx,
            new()
            {
                Dpi = 200
            },
            Sample.FontDirectory);

        // PNG IHDR width is a big-endian 32-bit int at byte offset 16.
        await Assert.That(Width(high[0])).IsGreaterThan(Width(low[0]));
    }

    // Snapshotted as .txt, not .md: the repo's MarkdownSnippets content validation (run from the main
    // Tests project) scans every .md file and would reject the sample letter's ordinary prose.
    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public Task ToMarkdown_Snapshot(InputFormat source) =>
        Verify(ConversionService.ToMarkdown(Sample.BytesFor(source), source), extension: "txt");

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public Task ToText_Snapshot(InputFormat source) =>
        Verify(ConversionService.ToText(Sample.BytesFor(source), source), extension: "txt");

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public Task ToHtml_Snapshot(InputFormat source) =>
        Verify(ConversionService.ToHtml(Sample.BytesFor(source), source), extension: "html");

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task ToPdf_ProducesPdfSignature(InputFormat source)
    {
        var pdf = ConversionService.ToPdf(Sample.BytesFor(source), source, Sample.FontDirectory);

        await Assert.That(pdf.Length).IsGreaterThan(5);
        await Assert.That(Encoding.ASCII.GetString(pdf, 0, 5)).IsEqualTo("%PDF-");
    }

    // BuildDownload's PNG branch collapses to a single .png for a one-page source and a .zip otherwise;
    // assert whichever matches the sample's actual page count so the test isn't coupled to an exact number.
    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task BuildDownload_Png_SingleFileOrZipByPageCount(InputFormat source)
    {
        var bytes = Sample.BytesFor(source);
        var pageCount = ConversionService.RenderPngPages(bytes, source, new(), Sample.FontDirectory).Count;
        var payload = ConversionService.BuildDownload(bytes, source, OutputFormat.Png, new(), Sample.FontDirectory);

        if (pageCount > 1)
        {
            await Assert.That(payload.Extension).IsEqualTo(".zip");
            await Assert.That(payload.ContentType).IsEqualTo("application/zip");
            // Zip local-file-header signature "PK\x03\x04".
            await Assert.That(payload.Bytes[..4]).IsEquivalentTo(new byte[]
            {
                0x50,
                0x4B,
                0x03,
                0x04
            });
        }
        else
        {
            await Assert.That(payload.Extension).IsEqualTo(".png");
            await Assert.That(payload.Bytes[..8]).IsEquivalentTo(pngSignature);
        }
    }

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task BuildDownload_Markdown_IsMarkdownFile(InputFormat source)
    {
        var payload = ConversionService.BuildDownload(Sample.BytesFor(source), source, OutputFormat.Markdown, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".md");
        await Assert.That(payload.ContentType).IsEqualTo("text/markdown");
        await Assert.That(Encoding.UTF8.GetString(payload.Bytes).Length).IsGreaterThan(0);
    }

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task BuildDownload_Html_IsHtmlFile(InputFormat source)
    {
        var payload = ConversionService.BuildDownload(Sample.BytesFor(source), source, OutputFormat.Html, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".html");
        await Assert.That(payload.ContentType).IsEqualTo("text/html");
        await Assert.That(Encoding.UTF8.GetString(payload.Bytes)).Contains("<html");
    }

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task BuildDownload_Text_IsTextFile(InputFormat source)
    {
        var payload = ConversionService.BuildDownload(Sample.BytesFor(source), source, OutputFormat.Text, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".txt");
        await Assert.That(payload.ContentType).IsEqualTo("text/plain");
    }

    [Test]
    [MethodDataSource(typeof(Sample), nameof(Sample.Formats))]
    public async Task BuildDownload_Pdf_IsPdfFile(InputFormat source)
    {
        var payload = ConversionService.BuildDownload(Sample.BytesFor(source), source, OutputFormat.Pdf, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".pdf");
        await Assert.That(payload.ContentType).IsEqualTo("application/pdf");
        await Assert.That(Encoding.ASCII.GetString(payload.Bytes, 0, 5)).IsEqualTo("%PDF-");
    }

    static int Width(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
}
