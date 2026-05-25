namespace Morph;

/// <summary>
/// Converts DOCX documents to vector-text PDF using PdfSharp.
/// </summary>
public sealed class PdfDocumentConverter
{
    /// <summary>Converts a DOCX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string docxPath, ConversionOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToPdf(stream, options);
    }

    /// <summary>Converts a DOCX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream docxStream, ConversionOptions? options = null)
    {
        options ??= new();
        var document = new DocumentParser(options.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        return PdfRenderer.Render(document, options);
    }

    /// <summary>Converts a DOCX file and writes the resulting PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string docxPath, string outputPdfPath, ConversionOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(docxPath, options));
}
