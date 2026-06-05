namespace Morph;

/// <summary>
/// Converts DOCX documents to vector-text PDF using PdfSharp. For parse-once / export-many
/// workflows, prefer <see cref="WordDocument"/> plus its <c>ExportToPdf</c> extension.
/// </summary>
public sealed class PdfDocumentConverter
{
    /// <summary>Converts a DOCX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string docxPath, PdfExportOptions? options = null)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToPdf(stream, options);
    }

    /// <summary>Converts a DOCX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream docxStream, PdfExportOptions? options = null)
    {
        var document = new DocumentParser(options?.DefaultFont ?? DefaultFontSettings.DefaultFont).Parse(docxStream);
        return PdfRenderer.Render(document, options);
    }

    /// <summary>Converts a DOCX file and writes the resulting PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string docxPath, string outputPdfPath, PdfExportOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(docxPath, options));

    /// <summary>Converts a DOCX stream and writes the resulting PDF to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream docxStream, Stream output, PdfExportOptions? options = null)
    {
        var bytes = ConvertToPdf(docxStream, options);
        output.Write(bytes, 0, bytes.Length);
    }
}
