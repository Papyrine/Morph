/// <summary>
/// Tests for header/footer output in the HTML export. HTML has no pagination, so each is emitted
/// once — but dropping them entirely loses whatever the template put there, which for a marked-up
/// document is the classification banner every rendered page carries.
/// </summary>
public class HtmlHeaderFooterExportTests
{
    static HeaderFooterContent Content(params DocumentElement[] elements) =>
        new() {Elements = elements};

    static ParsedDocument WithHeaderFooter(
        HeaderFooterContent? header = null,
        HeaderFooterContent? footer = null,
        HeaderFooterContent? firstPageHeader = null,
        HeaderFooterContent? firstPageFooter = null) =>
        new()
        {
            PageSettings = new(),
            Elements = [Para(TextRun("Body"))],
            Header = header,
            Footer = footer,
            FirstPageHeader = firstPageHeader,
            FirstPageFooter = firstPageFooter
        };

    [Test]
    public async Task HeaderAndFooter_AreEmittedOnceAroundTheBody()
    {
        var html = HtmlExporter.Export(WithHeaderFooter(
            header: Content(Para(TextRun("Top marking"))),
            footer: Content(Para(TextRun("Bottom marking")))));

        await Assert.That(html).Contains("<header class=\"doc-header\">");
        await Assert.That(html).Contains("Top marking");
        await Assert.That(html).Contains("<footer class=\"doc-footer\">");
        await Assert.That(html).Contains("Bottom marking");
        // Header before body before footer.
        await Assert.That(html.IndexOf("Top marking", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf("Body", StringComparison.Ordinal));
        await Assert.That(html.IndexOf("Body", StringComparison.Ordinal))
            .IsLessThan(html.IndexOf("Bottom marking", StringComparison.Ordinal));
    }

    [Test]
    public async Task NoHeaderOrFooter_EmitsNeitherWrapper()
    {
        var html = HtmlExporter.Export(WithHeaderFooter());

        await Assert.That(html).DoesNotContain("doc-header\">");
        await Assert.That(html).DoesNotContain("doc-footer\">");
    }

    /// <summary>
    /// The default wins over the first-page variant: it is what most of the document carries.
    /// </summary>
    [Test]
    public async Task DefaultHeader_WinsOverFirstPage()
    {
        var html = HtmlExporter.Export(WithHeaderFooter(
            header: Content(Para(TextRun("Default header"))),
            firstPageHeader: Content(Para(TextRun("Cover header")))));

        await Assert.That(html).Contains("Default header");
        await Assert.That(html).DoesNotContain("Cover header");
    }

    /// <summary>
    /// A blank first-page header is Word's way of suppressing the header on page 1, so an empty
    /// one must not mask a real default — the choice tests for CONTENT, not for null.
    /// </summary>
    [Test]
    public async Task BlankDefaultHeader_FallsBackToFirstPage()
    {
        var html = HtmlExporter.Export(WithHeaderFooter(
            header: Content(Para()),
            firstPageHeader: Content(Para(TextRun("Cover header")))));

        await Assert.That(html).Contains("Cover header");
    }

    /// <summary>A header holding only an empty paragraph produces no wrapper at all.</summary>
    [Test]
    public async Task BlankHeaderEverywhere_EmitsNoWrapper()
    {
        var html = HtmlExporter.Export(WithHeaderFooter(header: Content(Para())));

        await Assert.That(html).DoesNotContain("doc-header\">");
    }
}
