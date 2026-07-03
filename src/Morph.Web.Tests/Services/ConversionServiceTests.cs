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
    public async Task CanRead_DocxOnly()
    {
        await Assert.That(ConversionService.CanRead("report.docx")).IsTrue();
        await Assert.That(ConversionService.CanRead("REPORT.DOCX")).IsTrue();
        await Assert.That(ConversionService.CanRead("notes.txt")).IsFalse();
        await Assert.That(ConversionService.CanRead("image.png")).IsFalse();
    }

    [Test]
    public async Task RenderPngPages_ProducesAPngPerPage()
    {
        var pages = ConversionService.RenderPngPages(Sample.DocxBytes, new() { Dpi = 96 }, Sample.FontDirectory);

        await Assert.That(pages.Count).IsGreaterThanOrEqualTo(1);
        foreach (var page in pages)
        {
            await Assert.That(page[..8]).IsEquivalentTo(pngSignature);
        }
    }

    [Test]
    public async Task RenderPngPages_HigherDpi_ProducesWiderImage()
    {
        var low = ConversionService.RenderPngPages(Sample.DocxBytes, new() { Dpi = 96 }, Sample.FontDirectory);
        var high = ConversionService.RenderPngPages(Sample.DocxBytes, new() { Dpi = 200 }, Sample.FontDirectory);

        // PNG IHDR width is a big-endian 32-bit int at byte offset 16.
        await Assert.That(Width(high[0])).IsGreaterThan(Width(low[0]));
    }

    // Snapshotted as .txt, not .md: the repo's MarkdownSnippets content validation (run from the main
    // Tests project) scans every .md file and would reject the sample letter's ordinary prose.
    [Test]
    public Task ToMarkdown_Snapshot() =>
        Verify(ConversionService.ToMarkdown(Sample.DocxBytes), extension: "txt");

    [Test]
    public Task ToText_Snapshot() =>
        Verify(ConversionService.ToText(Sample.DocxBytes), extension: "txt");

    [Test]
    public Task ToHtml_Snapshot() =>
        Verify(ConversionService.ToHtml(Sample.DocxBytes), extension: "html");

    [Test]
    public async Task ToPdf_ProducesPdfSignature()
    {
        var pdf = ConversionService.ToPdf(Sample.DocxBytes, Sample.FontDirectory);

        await Assert.That(pdf.Length).IsGreaterThan(5);
        await Assert.That(Encoding.ASCII.GetString(pdf, 0, 5)).IsEqualTo("%PDF-");
    }

    // BuildDownload's PNG branch collapses to a single .png for a one-page document and a .zip otherwise;
    // assert whichever matches the sample's actual page count so the test isn't coupled to an exact number.
    [Test]
    public async Task BuildDownload_Png_SingleFileOrZipByPageCount()
    {
        var pageCount = ConversionService.RenderPngPages(Sample.DocxBytes, new(), Sample.FontDirectory).Count;
        var payload = ConversionService.BuildDownload(Sample.DocxBytes, OutputFormat.Png, new(), Sample.FontDirectory);

        if (pageCount > 1)
        {
            await Assert.That(payload.Extension).IsEqualTo(".zip");
            await Assert.That(payload.ContentType).IsEqualTo("application/zip");
            // Zip local-file-header signature "PK\x03\x04".
            await Assert.That(payload.Bytes[..4]).IsEquivalentTo(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        }
        else
        {
            await Assert.That(payload.Extension).IsEqualTo(".png");
            await Assert.That(payload.Bytes[..8]).IsEquivalentTo(pngSignature);
        }
    }

    [Test]
    public async Task BuildDownload_Markdown_IsMarkdownFile()
    {
        var payload = ConversionService.BuildDownload(Sample.DocxBytes, OutputFormat.Markdown, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".md");
        await Assert.That(payload.ContentType).IsEqualTo("text/markdown");
        await Assert.That(Encoding.UTF8.GetString(payload.Bytes).Length).IsGreaterThan(0);
    }

    [Test]
    public async Task BuildDownload_Html_IsHtmlFile()
    {
        var payload = ConversionService.BuildDownload(Sample.DocxBytes, OutputFormat.Html, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".html");
        await Assert.That(payload.ContentType).IsEqualTo("text/html");
        await Assert.That(Encoding.UTF8.GetString(payload.Bytes)).Contains("<html");
    }

    [Test]
    public async Task BuildDownload_Text_IsTextFile()
    {
        var payload = ConversionService.BuildDownload(Sample.DocxBytes, OutputFormat.Text, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".txt");
        await Assert.That(payload.ContentType).IsEqualTo("text/plain");
    }

    [Test]
    public async Task BuildDownload_Pdf_IsPdfFile()
    {
        var payload = ConversionService.BuildDownload(Sample.DocxBytes, OutputFormat.Pdf, new(), Sample.FontDirectory);

        await Assert.That(payload.Extension).IsEqualTo(".pdf");
        await Assert.That(payload.ContentType).IsEqualTo("application/pdf");
        await Assert.That(Encoding.ASCII.GetString(payload.Bytes, 0, 5)).IsEqualTo("%PDF-");
    }

    static int Width(byte[] png) =>
        (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
}
