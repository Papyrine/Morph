using Morph.PDFium;

/// <summary>
/// The PNG and PDF outputs come from one layout, so they must agree on it: every Word scenario has the same
/// pages at the same sizes either way, and where the text is plain enough to follow, every letter the PDF sets
/// stands on a line the layout placed — the line the raster painters draw. A painter that moved text or a
/// converter that paginated on its own would fail here before a reference comparison noticed.
/// </summary>
public class PngPdfAgreementTests
{
    static readonly string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static string Input(string scenario) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", scenario, "input.docx");

    public static IEnumerable<string> Scenarios() =>
        ScenarioInputs.Directories(ScenarioFormat.Word).Select(ScenarioInputs.ScenarioName).Order();

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task Pages_agree(string scenario)
    {
        // 18 DPI keeps the raster cheap: a page is a quarter of a pixel per point, which is all a size check needs.
        const int dpi = 18;
        var images = new SkiaDocumentConverter().ConvertToImageData(Input(scenario), new() {FontDirectory = fonts, Dpi = dpi});
        using var pdf = PdfiumDocument.Load(
            PdfDocumentConverter.ConvertToPdf(
                Input(scenario),
                new()
                {
                    FontDirectory = fonts
                }));
        var sizes = pdf.GetPageSizes();

        await Assert.That(sizes.Count).IsEqualTo(images.Count);
        for (var index = 0; index < images.Count; index++)
        {
            var (width, height) = PngSize(images[index]);
            await Assert.That((double) width).IsEqualTo(sizes[index].Width * dpi / 72).Within(1.0);
            await Assert.That((double) height).IsEqualTo(sizes[index].Height * dpi / 72).Within(1.0);
        }
    }

    // Plain text throughout — no text boxes, rotated cells or WordArt, whose letters a line collector would
    // have to follow through their own transforms.
    [Test]
    [Arguments("multiple_pages")]
    [Arguments("long_paragraph")]
    [Arguments("header_footer")]
    [Arguments("complex_tables")]
    [Arguments("two_columns")]
    [Arguments("page_numbers")]
    public async Task Pdf_text_stands_on_the_laid_out_lines(string scenario)
    {
        var document = new DocumentParser().Parse(Input(scenario));
        var laidOut = new Fragmenter(LayoutTestFonts.Measurer, document.Compatibility)
            .Layout(
                document.Elements, document.PageSettings, document.Header, document.Footer,
                document.FirstPageHeader, document.FirstPageFooter, document.EvenPageHeader, document.EvenPageFooter,
                DocumentNotes.From(document));
        using var pdf = PdfiumDocument.Load(
            PdfDocumentConverter.ConvertToPdf(
                Input(scenario),
                new()
                {
                    FontDirectory = fonts
                }));

        await Assert.That(pdf.PageCount).IsEqualTo(laidOut.Pages.Count);

        var strays = new List<string>();
        var checkedLetters = 0;
        for (var index = 0; index < pdf.PageCount; index++)
        {
            var baselines = Lines(laidOut.Pages[index].Items).Select(_ => _.Baseline).ToList();
            var height = pdf.GetPageSize(index).Height;
            using var page = pdf.LoadPage(index);
            foreach (var letter in page.GetChars().Where(_ => char.IsLetterOrDigit(_.Char)))
            {
                checkedLetters++;
                var baseline = height - letter.Origin.Y;
                if (!baselines.Exists(_ => Math.Abs(_ - baseline) <= 0.5))
                {
                    strays.Add($"page {index + 1}: '{letter.Char}' at baseline {baseline:F2}");
                }
            }
        }

        await Assert.That(checkedLetters).IsGreaterThan(100);
        await Assert.That(strays).IsEmpty();
    }

    static IEnumerable<PlacedLine> Lines(IEnumerable<PlacedItem> items)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case PlacedLine line:
                    yield return line;
                    break;
                case PlacedTableRow row:
                    foreach (var line in row.Cells.SelectMany(_ => Lines(_.Content)))
                    {
                        yield return line;
                    }

                    break;
                case PlacedClipGroup group:
                    foreach (var line in Lines(group.Items))
                    {
                        yield return line;
                    }

                    break;
            }
        }
    }

    static (int Width, int Height) PngSize(byte[] png) =>
        ((png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19],
            (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
}
