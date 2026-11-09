/// <summary>
/// <see cref="PdfExportOptions.OnProgress"/> and <see cref="ImageExportOptions.OnProgress"/> tell a host
/// how far an export has got: each page as layout finishes it, with no total yet, then each page as it
/// is drawn, as page x of the total. Reports are compared as text — <c>L3</c> for page 3 laid out,
/// <c>P3/18</c> for page 3 of 18 drawn — so a failure shows the whole sequence.
/// </summary>
public class ExportProgressTests
{
    static string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static string Input(string path) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", path);

    static PdfExportOptions Options(List<ExportProgress> reports, PageRange? pages = null) =>
        new()
        {
            FontDirectory = fonts,
            UseLetterPageSize = false,
            Pages = pages,
            OnProgress = reports.Add
        };

    // A low resolution keeps the image tests quick: progress does not depend on it.
    static ImageExportOptions ImageOptions(List<ExportProgress> reports, PageRange? pages = null, int parallelism = 1) =>
        new()
        {
            FontDirectory = fonts,
            UseLetterPageSize = false,
            Dpi = 20,
            Pages = pages,
            MaxDegreeOfParallelism = parallelism,
            OnProgress = reports.Add
        };

    [Test]
    public async Task Skia_word_images_report_each_page_laid_out_then_painted()
    {
        var reports = new List<ExportProgress>();

        new SkiaDocumentConverter().ConvertToImageData(Input("word/business-plans/12/input.docx"), ImageOptions(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    [Test]
    public async Task ImageSharp_word_images_report_each_page_laid_out_then_painted()
    {
        var reports = new List<ExportProgress>();

        new ImageSharpDocumentConverter().ConvertToImageData(Input("word/business-plans/12/input.docx"), ImageOptions(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    [Test]
    public async Task Excel_images_report_each_page_laid_out_then_painted()
    {
        var reports = new List<ExportProgress>();

        new SkiaExcelConverter().ConvertToImageData(Input("excel/personal-budget/input.xlsx"), ImageOptions(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(4));
    }

    [Test]
    public async Task PowerPoint_images_report_each_slide_laid_out_then_painted()
    {
        var reports = new List<ExportProgress>();

        new ImageSharpPowerPointConverter().ConvertToImageData(Input("powerpoint/pebble/input.pptx"), ImageOptions(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(12));
    }

    // Pages finish in no fixed order across threads, but the count is taken under one lock, so the
    // host still sees 1..N, one call at a time.
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Parallel_painting_still_counts_up_one_page_at_a_time(bool skia)
    {
        var reports = new List<ExportProgress>();
        var inside = 0;
        var overlapped = false;
        var options = ImageOptions(reports, parallelism: 4) with
        {
            OnProgress = _ =>
            {
                if (Interlocked.Increment(ref inside) > 1)
                {
                    overlapped = true;
                }

                reports.Add(_);
                Thread.Sleep(1);
                Interlocked.Decrement(ref inside);
            }
        };
        var input = Input("word/business-plans/12/input.docx");

        if (skia)
        {
            new SkiaDocumentConverter().ConvertToImageData(input, options);
        }
        else
        {
            new ImageSharpDocumentConverter().ConvertToImageData(input, options);
        }

        await Assert.That(overlapped).IsFalse();
        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    [Test]
    public async Task An_image_page_range_paints_only_its_own_pages()
    {
        var reports = new List<ExportProgress>();

        new SkiaDocumentConverter().ConvertToImageData(Input("word/business-plans/12/input.docx"), ImageOptions(reports, new(3, 5)));

        await Assert.That(Text(reports.Where(_ => _.Stage == ExportStage.Paint))).IsEqualTo("P1/3 P2/3 P3/3");
        await Assert.That(reports.Count(_ => _.Stage == ExportStage.Layout)).IsEqualTo(18);
    }

    static string Text(IEnumerable<ExportProgress> reports) =>
        string.Join(" ", reports.Select(Text));

    static string Text(ExportProgress report)
    {
        if (report.Stage == ExportStage.Layout)
        {
            return $"L{report.Page}{(report.Total == null ? "" : "/" + report.Total)}";
        }

        return $"P{report.Page}/{report.Total?.ToString() ?? "?"}";
    }

    // Layout 1..pages with no total, then paint 1..pages of pages.
    static string Expected(int pages) =>
        Text(
        [
            .. Enumerable.Range(1, pages).Select(_ => new ExportProgress(ExportStage.Layout, _, null)),
            .. Enumerable.Range(1, pages).Select(_ => new ExportProgress(ExportStage.Paint, _, pages))
        ]);

    [Test]
    public async Task Word_reports_each_page_laid_out_then_drawn()
    {
        var reports = new List<ExportProgress>();

        PdfDocumentConverter.ConvertToPdf(Input("word/business-plans/12/input.docx"), Options(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    [Test]
    public async Task Excel_reports_each_page_laid_out_then_drawn()
    {
        var reports = new List<ExportProgress>();

        PdfExcelConverter.ConvertToPdf(Input("excel/personal-budget/input.xlsx"), Options(reports));

        // Four pages whole; the scenario snapshots stop at two (ScenarioInputs.Pages).
        await Assert.That(Text(reports)).IsEqualTo(Expected(4));
    }

    [Test]
    public async Task PowerPoint_reports_each_slide_laid_out_then_drawn()
    {
        var reports = new List<ExportProgress>();

        PdfPowerPointConverter.ConvertToPdf(Input("powerpoint/pebble/input.pptx"), Options(reports));

        // Twelve slides whole; the scenario snapshots stop at two (ScenarioInputs.Pages).
        await Assert.That(Text(reports)).IsEqualTo(Expected(12));
    }

    [Test]
    public async Task A_parsed_document_reports_through_its_export()
    {
        var reports = new List<ExportProgress>();
        var document = new WordDocument(Input("word/business-plans/12/input.docx"));

        document.ExportToPdf(Options(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    // Layout still walks every page, but paint counts only the pages the range draws.
    [Test]
    public async Task A_page_range_paints_only_its_own_pages()
    {
        var reports = new List<ExportProgress>();

        PdfDocumentConverter.ConvertToPdf(Input("word/business-plans/12/input.docx"), Options(reports, new(3, 5)));

        await Assert.That(Text(reports.Where(_ => _.Stage == ExportStage.Paint))).IsEqualTo("P1/3 P2/3 P3/3");
        await Assert.That(reports.Count(_ => _.Stage == ExportStage.Layout)).IsEqualTo(18);
    }

    // A PAGE field in the body lays the document out twice; the second pass must not start the count
    // over, so layout's numbers only ever rise.
    [Test]
    public async Task Layout_never_counts_backwards_when_a_page_field_lays_out_twice()
    {
        var reports = new List<ExportProgress>();
        using var docx = PageTextTests.BuildDocument(
            PageTextTests.Paragraph("First"),
            PageTextTests.PageBreak(),
            PageTextTests.Field(" PAGE "));

        PdfDocumentConverter.ConvertToPdf(docx, Options(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(2));
    }

    [Test]
    public async Task No_callback_is_no_change()
    {
        var input = Input("word/business-plans/12/input.docx");
        var withReports = PdfDocumentConverter.ConvertToPdf(input, Options([]));
        var without = PdfDocumentConverter.ConvertToPdf(input, Options([]) with { OnProgress = null });

        await Assert.That(withReports.SequenceEqual(without)).IsTrue();
    }
}
