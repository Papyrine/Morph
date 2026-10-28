/// <summary>
/// Painting pages on several threads (<see cref="ImageExportOptions.MaxDegreeOfParallelism"/>) changes
/// how fast the pages come, never what they are or the order they come in: each worker paints with its
/// own render context, so every page is byte-identical to the serial path's. business-plans/12 is one of the
/// corpus's longest documents, 19 pages of text, tables, pictures and shapes.
/// </summary>
public class ParallelPaintingTests
{
    static readonly string input = Path.Combine(ProjectFiles.ProjectDirectory, "Inputs", "word", "business-plans", "12", "input.docx");

    static readonly string fonts = Path.GetFullPath(Path.Combine(ProjectFiles.ProjectDirectory, "..", "Fonts"));

    static ImageExportOptions Options(int parallelism) =>
        new() { Dpi = 48, FontDirectory = fonts, MaxDegreeOfParallelism = parallelism };

    [Test]
    public async Task Skia_paints_the_same_pages_in_the_same_order_in_parallel()
    {
        var serial = new SkiaDocumentConverter().ConvertToImageData(input, Options(1));
        var parallel = new SkiaDocumentConverter().ConvertToImageData(input, Options(4));

        await Assert.That(parallel.Count).IsEqualTo(serial.Count);
        for (var page = 0; page < serial.Count; page++)
        {
            await Assert.That(parallel[page].SequenceEqual(serial[page])).IsTrue().Because($"page {page + 1}");
        }
    }

    [Test]
    public async Task ImageSharp_paints_the_same_pages_in_the_same_order_in_parallel()
    {
        var serial = new ImageSharpDocumentConverter().ConvertToImageData(input, Options(1));
        var parallel = new ImageSharpDocumentConverter().ConvertToImageData(input, Options(4));

        await Assert.That(parallel.Count).IsEqualTo(serial.Count);
        for (var page = 0; page < serial.Count; page++)
        {
            await Assert.That(parallel[page].SequenceEqual(serial[page])).IsTrue().Because($"page {page + 1}");
        }
    }
}
