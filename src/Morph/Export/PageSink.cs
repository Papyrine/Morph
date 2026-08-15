/// <summary>
/// The two destinations a rendered page run writes to: a directory of numbered PNGs, or a list of
/// PNG byte arrays.
/// </summary>
/// <remarks>
/// <para>
/// Every raster converter — <c>DocumentConverter</c>, <c>HtmlConverter</c>,
/// <c>PowerPointConverter</c>, <c>ExcelConverter</c> — offers the same pair of outputs over its own
/// input format, and carried its own copy of both bodies. The only thing that differed between the
/// four was how the document got parsed; the sink either side of that was identical, down to the
/// <c>page_0001.png</c> format string.
/// </para>
/// <para>
/// A plain static rather than an extension: the receiver would have to be the <c>RenderPages</c>
/// delegate, and an extension on <c>Func&lt;…&gt;</c> reads worse at the call site than naming the
/// destination does.
/// </para>
/// </remarks>
static class PageSink
{
    /// <summary>
    /// Runs <paramref name="renderPages"/> against a sink that writes each page to
    /// <paramref name="outputDirectory"/> as <c>page_NNNN.png</c>, creating the directory first.
    /// </summary>
    public static ConversionResult ToDirectory(
        string outputDirectory,
        Func<Action<Action<Stream>>, int> renderPages)
    {
        Directory.CreateDirectory(outputDirectory);

        var imagePaths = new List<string>();
        var pageIndex = 0;
        var pageCount = renderPages(
            writePng =>
            {
                var filePath = Path.Combine(outputDirectory, $"page_{++pageIndex:D4}.png");
                imagePaths.Add(filePath);
                using var fileStream = File.Create(filePath);
                writePng(fileStream);
            });

        return new(imagePaths, pageCount);
    }

    /// <summary>
    /// Runs <paramref name="renderPages"/> against a sink that collects each page into memory.
    /// </summary>
    public static IReadOnlyList<byte[]> ToMemory(Action<Action<Action<Stream>>> renderPages)
    {
        var imageData = new List<byte[]>();
        renderPages(
            writePng =>
            {
                using var stream = new MemoryStream();
                writePng(stream);
                imageData.Add(stream.ToArray());
            });

        return imageData;
    }
}
