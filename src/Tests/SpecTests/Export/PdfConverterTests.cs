/// <summary>
/// Smoke tests for the DOCX → PDF backend: real documents must convert to a valid, non-empty PDF
/// without throwing. (PDF bytes are not byte-stable, so these assert structure rather than snapshot.)
/// </summary>
public class PdfConverterTests
{
    static readonly string fontsDirectory = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    public static IEnumerable<string> SampleInputs()
    {
        var inputs = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs");
        foreach (var name in new[] {"bold_text", "align_center", "hyperlinks"})
        {
            var path = Path.Combine(inputs, name, "input.docx");
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(SampleInputs))]
    public async Task ProducesValidPdf(string inputPath)
    {
        var pdf = PdfDocumentConverter.ConvertToPdf(inputPath, new() {FontDirectory = fontsDirectory});

        await Assert.That(pdf.Length).IsGreaterThan(1000);
        // Every PDF starts with the "%PDF-" header.
        var header = System.Text.Encoding.ASCII.GetString(pdf, 0, 5);
        await Assert.That(header).IsEqualTo("%PDF-");
    }

    [Test]
    public async Task OutputIsDeterministic()
    {
        var input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "bullet_list", "input.docx");
        var first = PdfDocumentConverter.ConvertToPdf(input, new() {FontDirectory = fontsDirectory});
        var second = PdfDocumentConverter.ConvertToPdf(input, new() {FontDirectory = fontsDirectory});

        await Assert.That(second).IsEquivalentTo(first);
    }

    [Test]
    public async Task MultiPageDocumentPaginates()
    {
        var inputPath = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "agendas-minutes", "02", "input.docx");
        if (!File.Exists(inputPath))
        {
            return;
        }

        var pdf = PdfDocumentConverter.ConvertToPdf(inputPath, new() {FontDirectory = fontsDirectory});

        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(pdf), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        await Assert.That(document.PageCount).IsGreaterThanOrEqualTo(2);
    }
}
