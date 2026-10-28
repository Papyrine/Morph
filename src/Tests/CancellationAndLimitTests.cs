/// <summary>
/// A conversion can be stopped (each converter's <c>cancel</c> parameter), and no page can ask for more
/// pixels than <see cref="ImageExportOptions.MaxPagePixels"/>: one that would is painted at the highest
/// resolution that fits instead.
/// </summary>
public class CancellationAndLimitTests
{
    static readonly string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static string Input(string format, string scenario) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", format, scenario, $"input.{(format == "word" ? "docx" : format == "excel" ? "xlsx" : "pptx")}");

    static Cancel Cancelled()
    {
        using var source = new CancelSource();
        source.Cancel();
        return source.Token;
    }

    [Test]
    [Arguments(1)]
    [Arguments(4)]
    public async Task A_cancelled_word_conversion_stops(int parallelism) =>
        await Assert.That(() => new SkiaDocumentConverter().ConvertToImageData(Input("word", "business-plans/12"), new() { FontDirectory = fonts, MaxDegreeOfParallelism = parallelism }, Cancelled()))
            .Throws<OperationCanceledException>();

    [Test]
    public async Task A_cancelled_excel_conversion_stops() =>
        await Assert.That(() => new ImageSharpExcelConverter().ConvertToImageData(Input("excel", "personal-budget"), new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();

    [Test]
    public async Task A_cancelled_powerpoint_conversion_stops()
    {
        var deck = Directory.GetFiles(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "powerpoint"), "input.pptx", SearchOption.AllDirectories).Order().First();

        await Assert.That(() => new SkiaPowerPointConverter().ConvertToImageData(deck, new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task A_cancelled_html_conversion_stops() =>
        await Assert.That(async () => await new SkiaHtmlConverter().ConvertToImageData("<p>Hello</p>", new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();

    [Test]
    public async Task A_cancelled_word_pdf_conversion_stops() =>
        await Assert.That(() => PdfDocumentConverter.ConvertToPdf(Input("word", "business-plans/12"), new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();

    [Test]
    public async Task A_cancelled_excel_pdf_conversion_stops() =>
        await Assert.That(() => PdfExcelConverter.ConvertToPdf(Input("excel", "personal-budget"), new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();

    [Test]
    public async Task A_cancelled_powerpoint_pdf_conversion_stops()
    {
        var deck = Directory.GetFiles(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "powerpoint"), "input.pptx", SearchOption.AllDirectories).Order().First();

        await Assert.That(() => PdfPowerPointConverter.ConvertToPdf(deck, new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task A_cancelled_html_pdf_conversion_stops() =>
        await Assert.That(async () => await PdfHtmlConverter.ConvertToPdf("<p>Hello</p>", new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();

    // A parsed document's export checks the token itself, since no parse stands before it.
    [Test]
    public async Task A_cancelled_pdf_export_of_a_parsed_document_stops()
    {
        var document = new WordDocument(Input("word", "complex_tables"));

        await Assert.That(() => document.ExportToPdf(new() { FontDirectory = fonts }, Cancelled()))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task An_ordinary_page_paints_at_the_resolution_asked_for()
    {
        var a4 = new PageSettings { WidthPoints = 595, HeightPoints = 842 };

        await Assert.That(new ImageExportOptions { Dpi = 600 }.EffectiveDpi([a4])).IsEqualTo(600);
        await Assert.That(new ImageExportOptions { Dpi = 1200, MaxPagePixels = 0 }.EffectiveDpi([a4])).IsEqualTo(1200);
    }

    [Test]
    public async Task A_page_beyond_the_budget_paints_at_the_highest_resolution_that_fits()
    {
        // A 14400pt square page (the largest the parser keeps) at 150 DPI would be 30000 x 30000 pixels.
        var huge = new PageSettings { WidthPoints = 14400, HeightPoints = 14400 };
        var dpi = new ImageExportOptions { Dpi = 150 }.EffectiveDpi([huge]);
        var side = 14400 * dpi / 72;

        await Assert.That((long) side * side).IsLessThanOrEqualTo(50_000_000);
        await Assert.That(dpi).IsGreaterThan(0);
    }

    [Test]
    public async Task A_crafted_page_size_renders_within_the_budget()
    {
        await using var zip = new ZipArchive(
            new MemoryStream(
                await File.ReadAllBytesAsync(Input("word", "complex_tables"))));
        var parts = zip.Entries.ToDictionary(_ => _.FullName, _ =>
        {
            using var stream = _.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        });
        var document = Regex.Replace(Encoding.UTF8.GetString(parts["word/document.xml"]), "<w:pgSz [^>]*/>", "<w:pgSz w:w=\"288000\" w:h=\"288000\"/>");
        parts["word/document.xml"] = Encoding.UTF8.GetBytes(document);
        using var output = new MemoryStream();
        await using (var rebuilt = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in parts)
            {
                await using var stream = await rebuilt.CreateEntry(name).OpenAsync();
                stream.Write(bytes);
            }
        }

        var page = new SkiaDocumentConverter().ConvertToImageData(new MemoryStream(output.ToArray()), new() { FontDirectory = fonts, Dpi = 150, MaxPagePixels = 4_000_000 })[0];
        var width = (page[16] << 24) | (page[17] << 16) | (page[18] << 8) | page[19];
        var height = (page[20] << 24) | (page[21] << 16) | (page[22] << 8) | page[23];

        await Assert.That((long) width * height).IsLessThanOrEqualTo(4_000_000);
    }
}
