/// <summary>
/// <see cref="PdfExportOptions.OnProgress"/> tells a host how far a PDF export has got: each page as
/// layout finishes it, with no total yet, then each page as it is drawn, as page x of the total.
/// Reports are compared as text — <c>L3</c> for page 3 laid out, <c>P3/18</c> for page 3 of 18 drawn —
/// so a failure shows the whole sequence.
/// </summary>
public class PdfProgressTests
{
    static readonly string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static string Input(string path) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", path);

    static PdfExportOptions Options(List<PdfExportProgress> reports, PageRange? pages = null) =>
        new()
        {
            FontDirectory = fonts,
            UseLetterPageSize = false,
            Pages = pages,
            OnProgress = reports.Add
        };

    static string Text(IEnumerable<PdfExportProgress> reports) =>
        string.Join(" ", reports.Select(Text));

    static string Text(PdfExportProgress report)
    {
        if (report.Stage == PdfExportStage.Layout)
        {
            return $"L{report.Page}{(report.Total == null ? "" : "/" + report.Total)}";
        }

        return $"P{report.Page}/{report.Total?.ToString() ?? "?"}";
    }

    // Layout 1..pages with no total, then paint 1..pages of pages.
    static string Expected(int pages) =>
        Text(
        [
            .. Enumerable.Range(1, pages).Select(_ => new PdfExportProgress(PdfExportStage.Layout, _, null)),
            .. Enumerable.Range(1, pages).Select(_ => new PdfExportProgress(PdfExportStage.Paint, _, pages))
        ]);

    [Test]
    public async Task Word_reports_each_page_laid_out_then_drawn()
    {
        var reports = new List<PdfExportProgress>();

        PdfDocumentConverter.ConvertToPdf(Input("word/business-plans/12/input.docx"), Options(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    [Test]
    public async Task Excel_reports_each_page_laid_out_then_drawn()
    {
        var reports = new List<PdfExportProgress>();

        PdfExcelConverter.ConvertToPdf(Input("excel/personal-budget/input.xlsx"), Options(reports));

        // Four pages whole; the scenario snapshots stop at two (ScenarioInputs.Pages).
        await Assert.That(Text(reports)).IsEqualTo(Expected(4));
    }

    [Test]
    public async Task PowerPoint_reports_each_slide_laid_out_then_drawn()
    {
        var reports = new List<PdfExportProgress>();

        PdfPowerPointConverter.ConvertToPdf(Input("powerpoint/pebble/input.pptx"), Options(reports));

        // Twelve slides whole; the scenario snapshots stop at two (ScenarioInputs.Pages).
        await Assert.That(Text(reports)).IsEqualTo(Expected(12));
    }

    [Test]
    public async Task A_parsed_document_reports_through_its_export()
    {
        var reports = new List<PdfExportProgress>();
        var document = new WordDocument(Input("word/business-plans/12/input.docx"));

        document.ExportToPdf(Options(reports));

        await Assert.That(Text(reports)).IsEqualTo(Expected(18));
    }

    // Layout still walks every page, but paint counts only the pages the range draws.
    [Test]
    public async Task A_page_range_paints_only_its_own_pages()
    {
        var reports = new List<PdfExportProgress>();

        PdfDocumentConverter.ConvertToPdf(Input("word/business-plans/12/input.docx"), Options(reports, new(3, 5)));

        await Assert.That(Text(reports.Where(_ => _.Stage == PdfExportStage.Paint))).IsEqualTo("P1/3 P2/3 P3/3");
        await Assert.That(reports.Count(_ => _.Stage == PdfExportStage.Layout)).IsEqualTo(18);
    }

    // A PAGE field in the body lays the document out twice; the second pass must not start the count
    // over, so layout's numbers only ever rise.
    [Test]
    public async Task Layout_never_counts_backwards_when_a_page_field_lays_out_twice()
    {
        var reports = new List<PdfExportProgress>();
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
