namespace WordRender;

/// <summary>
/// Converts DOCX documents to PNG images.
/// </summary>
public sealed class DocumentConverter
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

        // Parse the document
        var document = parser.Parse(docxStream);

        // Render to bitmaps
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale);
        using var renderer = new PageRenderer(context);

        var pages = renderer.RenderDocument(document);

        // Save pages as PNGs
        var imagePaths = new List<string>();

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var fileName = $"page_{i + 1:D4}.png";
            var filePath = Path.Combine(outputDirectory, fileName);

            using var image = SKImage.FromBitmap(page);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var fileStream = File.OpenWrite(filePath);
            data.SaveTo(fileStream);

            imagePaths.Add(filePath);
            page.Dispose();
        }

        return new(imagePaths, pages.Count);
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

        // Parse the document
        var document = parser.Parse(docxStream);

        // Render to bitmaps
        using var context = new RenderContext(document.PageSettings, options.Dpi, document.Compatibility, options.FontWidthScale);
        using var renderer = new PageRenderer(context);

        var pages = renderer.RenderDocument(document);

        // Encode pages to PNG data
        var imageData = new List<byte[]>();

        foreach (var page in pages)
        {
            using var image = SKImage.FromBitmap(page);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            imageData.Add(data.ToArray());
            page.Dispose();
        }

        return imageData;
    }
}

/// <summary>
/// Options for document conversion.
/// </summary>
/// <param name="Dpi">Image resolution in dots per inch. Default is 150.</param>
/// <param name="FontWidthScale">Scale factor for font width measurements. Default uses DefaultFontSettings.FontWidthScale.
/// Use values > 1.0 to make text wider (causes earlier line wrapping).
/// A value of 1.07 better matches Microsoft Word's text rendering.</param>
public sealed record ConversionOptions
{
    public int Dpi { get; init; } = 150;
    public double FontWidthScale { get; init; } = DefaultFontSettings.FontWidthScale;
}

/// <summary>
/// Result of a document conversion.
/// </summary>
/// <param name="ImagePaths">Paths to the generated PNG images.</param>
/// <param name="PageCount">Number of pages in the document.</param>
public sealed record ConversionResult(IReadOnlyList<string> ImagePaths, int PageCount);
