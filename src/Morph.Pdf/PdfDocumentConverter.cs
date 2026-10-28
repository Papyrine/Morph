namespace Morph;

/// <summary>
/// Converts DOCX documents to vector-text PDF using Krilla. For parse-once / export-many
/// workflows, prefer <see cref="WordDocument"/> plus its <c>ExportToPdf</c> extension.
/// </summary>
public sealed class PdfDocumentConverter
{
    /// <summary>Converts a DOCX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string docxPath, PdfExportOptions? options = null) =>
        ConvertToPdf(docxPath, options, Cancel.None);

    /// <summary>Converts a DOCX file to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(string docxPath, PdfExportOptions? options, Cancel cancel)
    {
        using var stream = File.OpenRead(docxPath);
        return ConvertToPdf(stream, options, cancel);
    }

    /// <summary>Converts a DOCX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream docxStream, PdfExportOptions? options = null) =>
        ConvertToPdf(docxStream, options, Cancel.None);

    /// <summary>Converts a DOCX stream to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(Stream docxStream, PdfExportOptions? options, Cancel cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var document = new DocumentParser(options?.DefaultFont ?? DefaultFontSettings.CustomizedDefaultFont, culture: options?.Culture).Parse(docxStream);
        return PdfRenderer.Render(document, options, cancel);
    }

    /// <summary>Converts a DOCX file and writes the resulting PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string docxPath, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(docxPath, options, cancel));

    /// <summary>Converts a DOCX stream and writes the resulting PDF to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream docxStream, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var bytes = ConvertToPdf(docxStream, options, cancel);
        output.Write(bytes, 0, bytes.Length);
    }
}
