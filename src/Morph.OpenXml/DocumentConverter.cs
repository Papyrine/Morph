using Morph;

namespace WordRender;

/// <summary>
/// Converts DOCX documents to PNG images.
/// </summary>
public abstract class DocumentConverter
{
    /// <summary>
    /// Converts a DOCX file to PNG images.
    /// </summary>
    /// <param name="docxPath">Path to the DOCX file.</param>
    /// <param name="outputDirectory">Directory where PNG files will be saved.</param>
    /// <param name="options">Conversion options (optional).</param>
    /// <returns>Result containing paths to generated images and page count.</returns>
    public ConversionResult ConvertToImages(string docxPath, string outputDirectory, ConversionOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToImages(stream, outputDirectory, options);
    }

    /// <summary>
    /// Converts a DOCX stream to PNG images.
    /// </summary>
    /// <param name="docxStream">Stream containing the DOCX document.</param>
    /// <param name="outputDirectory">Directory where PNG files will be saved.</param>
    /// <param name="options">Conversion options (optional).</param>
    /// <returns>Result containing paths to generated images and page count.</returns>
    public ConversionResult ConvertToImages(Stream docxStream, string outputDirectory, ConversionOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();
        Directory.CreateDirectory(outputDirectory);

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        var imagePaths = new List<string>();

        var pageIndex = 0;
        var pageCount = RenderPages(
            document,
            options,
            writePng =>
        {
            var filePath = Path.Combine(outputDirectory, $"page_{++pageIndex:D4}.png");
            imagePaths.Add(filePath);
            using var fs = File.Create(filePath);
            writePng(fs);
        });

        return new(imagePaths, pageCount);
    }

    /// <summary>
    /// Converts a DOCX file to PNG image data in memory.
    /// </summary>
    /// <param name="docxPath">Path to the DOCX file.</param>
    /// <param name="options">Conversion options (optional).</param>
    /// <returns>List of PNG image data for each page.</returns>
    public IReadOnlyList<byte[]> ConvertToImageData(string docxPath, ConversionOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToImageData(stream, options);
    }

    /// <summary>
    /// Converts a DOCX stream to PNG image data in memory.
    /// </summary>
    /// <param name="docxStream">Stream containing the DOCX document.</param>
    /// <param name="options">Conversion options (optional).</param>
    /// <returns>List of PNG image data for each page.</returns>
    public IReadOnlyList<byte[]> ConvertToImageData(Stream docxStream, ConversionOptions? options = null)
    {
        options ??= new();
        DefaultFontSettings.MarkRenderOccurred();

        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        var imageData = new List<byte[]>();

        RenderPages(document, options, writePng =>
        {
            using var ms = new MemoryStream();
            writePng(ms);
            imageData.Add(ms.ToArray());
        });

        return imageData;
    }

    /// <summary>
    /// Renders a parsed document by calling pageCallback for each page.
    /// The callback receives an action that writes PNG data to a stream.
    /// Returns the total page count.
    /// </summary>
    private protected abstract int RenderPages(ParsedDocument document, ConversionOptions options, Action<Action<Stream>> pageCallback);
}
