/// <summary>
/// The same input renders to the same bytes the second time, in a fresh converter: every input format to
/// PNG through both raster backends, and to PDF. The Verify baselines depend on this, but only catch a
/// render that drifts between runs on different days; here the two renders sit side by side, so state
/// a conversion leaves behind (a cache, a counter, a pooled buffer) cannot hide.
/// </summary>
public class RepeatRenderTests
{
    static readonly string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static string Input(string format, string scenario, string extension) =>
        Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", format, scenario, $"input.{extension}");

    static readonly string word = Input("word", "complex_tables", "docx");
    static readonly string excel = Input("excel", "personal-budget", "xlsx");

    static string Deck() =>
        Directory.GetFiles(Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "powerpoint"), "input.pptx", SearchOption.AllDirectories).Order().First();

    const string html = "<h1>Title</h1><p>Some <b>bold</b> text.</p><table border=\"1\"><tr><td>a</td><td>b</td></tr></table>";

    [Test]
    public async Task Word_png_is_byte_identical_on_a_second_render()
    {
        await Same(() => new SkiaDocumentConverter().ConvertToImageData(word, new() { FontDirectory = fonts }));
        await Same(() => new ImageSharpDocumentConverter().ConvertToImageData(word, new() { FontDirectory = fonts }));
    }

    [Test]
    public async Task Excel_png_is_byte_identical_on_a_second_render()
    {
        await Same(() => new SkiaExcelConverter().ConvertToImageData(excel, new() { FontDirectory = fonts }));
        await Same(() => new ImageSharpExcelConverter().ConvertToImageData(excel, new() { FontDirectory = fonts }));
    }

    [Test]
    public async Task PowerPoint_png_is_byte_identical_on_a_second_render()
    {
        var deck = Deck();
        await Same(() => new SkiaPowerPointConverter().ConvertToImageData(deck, new() { FontDirectory = fonts }));
        await Same(() => new ImageSharpPowerPointConverter().ConvertToImageData(deck, new() { FontDirectory = fonts }));
    }

    [Test]
    public async Task Html_png_is_byte_identical_on_a_second_render()
    {
        await Same(() => new SkiaHtmlConverter().ConvertToImageData(html, new() { FontDirectory = fonts }).GetAwaiter().GetResult());
        await Same(() => new ImageSharpHtmlConverter().ConvertToImageData(html, new() { FontDirectory = fonts }).GetAwaiter().GetResult());
    }

    [Test]
    public async Task Pdf_is_byte_identical_on_a_second_render()
    {
        var deck = Deck();
        await Same(() => [PdfDocumentConverter.ConvertToPdf(word, new() { FontDirectory = fonts })]);
        await Same(() => [PdfExcelConverter.ConvertToPdf(excel, new() { FontDirectory = fonts })]);
        await Same(() => [PdfPowerPointConverter.ConvertToPdf(deck, new() { FontDirectory = fonts })]);
        await Same(() => [PdfHtmlConverter.ConvertToPdf(html, new() { FontDirectory = fonts }).GetAwaiter().GetResult()]);
    }

    static async Task Same(Func<IReadOnlyList<byte[]>> render)
    {
        var first = render();
        var second = render();

        await Assert.That(second.Count).IsEqualTo(first.Count);
        for (var page = 0; page < first.Count; page++)
        {
            await Assert.That(second[page].SequenceEqual(first[page])).IsTrue().Because($"page {page + 1}");
        }
    }
}
