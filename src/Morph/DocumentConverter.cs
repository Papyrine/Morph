namespace WordRender;

/// <summary>
/// Converts DOCX documents to PNG images.
/// </summary>
public abstract class DocumentConverter
{
    DocumentParser parser = new();

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
        Directory.CreateDirectory(outputDirectory);

        var document = parser.Parse(docxStream);
        var imageData = RenderToImageData(document, options);

        var imagePaths = new List<string>();

        for (var i = 0; i < imageData.Count; i++)
        {
            var fileName = $"page_{i + 1:D4}.png";
            var filePath = Path.Combine(outputDirectory, fileName);
            File.WriteAllBytes(filePath, imageData[i]);
            imagePaths.Add(filePath);
        }

        return new(imagePaths, imageData.Count);
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

        var document = parser.Parse(docxStream);
        return RenderToImageData(document, options);
    }

    /// <summary>
    /// Renders a parsed document to PNG image data. Implemented by backend-specific converters.
    /// </summary>
    private protected abstract IReadOnlyList<byte[]> RenderToImageData(ParsedDocument document, ConversionOptions options);
}
