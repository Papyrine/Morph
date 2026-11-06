namespace Morph;

/// <summary>
/// Converts email, <c>.eml</c> or Outlook <c>.msg</c>, to vector-text PDF: the document
/// <see cref="EmailConverter"/> reads it as, rendered by <see cref="PdfDocumentConverter"/>. The date in
/// the header is written in <see cref="ExportOptions.Culture"/>.
/// </summary>
public sealed class PdfEmailConverter
{
    /// <summary>Converts an email file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string emailPath, PdfExportOptions? options = null) =>
        ConvertToPdf(emailPath, options, Cancel.None);

    /// <summary>Converts an email file to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(string emailPath, PdfExportOptions? options, Cancel cancel)
    {
        using var stream = File.OpenRead(emailPath);
        return ConvertToPdf(stream, options, cancel);
    }

    /// <summary>Converts an email stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream emailStream, PdfExportOptions? options = null) =>
        ConvertToPdf(emailStream, options, Cancel.None);

    /// <summary>Converts an email stream to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(Stream emailStream, PdfExportOptions? options, Cancel cancel)
    {
        cancel.ThrowIfCancellationRequested();
        using var docx = EmailConverter.ToDocx(emailStream, options?.Culture, cancel);
        return PdfDocumentConverter.ConvertToPdf(docx, options, cancel);
    }

    /// <summary>Converts an email file and writes the resulting PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string emailPath, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(emailPath, options, cancel));

    /// <summary>Converts an email stream and writes the resulting PDF to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream emailStream, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var bytes = ConvertToPdf(emailStream, options, cancel);
        output.Write(bytes, 0, bytes.Length);
    }
}
