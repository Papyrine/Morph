using Morph.PDFium;

/// <summary>
/// Email to PDF: a message read from <c>.eml</c> and from Outlook <c>.msg</c>, the same message saved in
/// both, with a header block (sender, date, recipients, subject, the attachments by name), an HTML body
/// with an inline <c>cid:</c> picture, a list and a table, and two attachments. The <c>.msg</c> keeps the
/// date in UTC, so its header differs from the <c>.eml</c>'s by the offset.
/// </summary>
public class PdfEmailConverterTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static string Input(string extension) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "SpecTests", "Email", $"quarterly-review{extension}");

    // The document the email is read as: the header block, then the body with its picture inlined.
    [Test]
    [Arguments(".eml")]
    [Arguments(".msg")]
    public async Task Html(string extension)
    {
        var html = EmailConverter.ConvertToHtml(Input(extension));

        await Verify(html, extension: "html")
            .UseParameters(extension);
    }

    [Test]
    [Arguments(".eml")]
    [Arguments(".msg")]
    public async Task Pdf(string extension)
    {
        ContainerOnly.Require();
        var pdf = PdfEmailConverter.ConvertToPdf(
            Input(extension),
            new()
            {
                FontDirectory = fontsDirectory
            });

        await Verify(pdf, extension: "pdf")
            .UseParameters(extension);
    }

    // What the page says, on any machine: the header and the body are in the PDF's text, the picture
    // shown in the body is not listed as an attachment.
    [Test]
    [Arguments(".eml")]
    [Arguments(".msg")]
    public async Task PdfText(string extension)
    {
        var pdf = PdfEmailConverter.ConvertToPdf(
            Input(extension),
            new()
            {
                FontDirectory = fontsDirectory
            });

        await Assert.That(Encoding.ASCII.GetString(pdf, 0, 5)).IsEqualTo("%PDF-");
        using var document = PdfiumDocument.Load(pdf, null);
        await Assert.That(document.PageCount).IsEqualTo(1);
        using var page = document.LoadPage(0);
        var text = page.GetText()!;
        await Assert.That(text).Contains("Quarterly review on Thursday");
        await Assert.That(text).Contains("Sam Taylor <sam.taylor@example.org>");
        await Assert.That(text).Contains("Priya Nair <priya.nair@example.org>, Alex Chen <alex.chen@example.org>");
        await Assert.That(text).Contains("Finance <finance@example.org>");
        await Assert.That(text).Contains("agenda.txt, figures.pdf");
        await Assert.That(text).DoesNotContain("chart.png");
        await Assert.That(text).Contains("Revenue is up 4%");
        await Assert.That(text).Contains("On plan");
    }

    [Test]
    public async Task Docx_IsAWordDocument()
    {
        using var email = File.OpenRead(Input(".eml"));
        using var docx = new MemoryStream();

        EmailConverter.ConvertToDocx(email, docx);

        docx.Position = 0;
        var text = new WordDocument(docx).ExportToMarkdown();
        await Assert.That(text).Contains("Quarterly review on Thursday");
        await Assert.That(text).Contains("agenda.txt, figures.pdf");
    }

    [Test]
    public async Task Cancelled_Throws()
    {
        using var cancel = new CancelSource();
        await cancel.CancelAsync();

        await Assert.That(() => PdfEmailConverter.ConvertToPdf(Input(".eml"), null, cancel.Token))
            .Throws<OperationCanceledException>();
    }
}
