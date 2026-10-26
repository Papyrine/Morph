namespace Morph;

/// <summary>
/// Converts PowerPoint presentations to vector-text PDF using Krilla, one PDF page per slide. For
/// parse-once / export-many workflows, prefer <see cref="PowerPointDocument"/> plus its
/// <c>ExportToPdf</c> extension.
/// </summary>
public sealed class PdfPowerPointConverter
{
    /// <summary>Converts a PPTX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string pptxPath, PdfExportOptions? options = null) =>
        ConvertToPdf(pptxPath, options, default);

    /// <summary>Converts a PPTX file to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(string pptxPath, PdfExportOptions? options, Cancel cancel)
    {
        using var stream = File.OpenRead(pptxPath);
        return ConvertToPdf(stream, options, cancel);
    }

    /// <summary>Converts a PPTX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream pptxStream, PdfExportOptions? options = null) =>
        ConvertToPdf(pptxStream, options, default);

    /// <summary>Converts a PPTX stream to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ConvertToPdf(Stream pptxStream, PdfExportOptions? options, Cancel cancel)
    {
        cancel.ThrowIfCancellationRequested();
        return PdfRenderer.Render(PowerPointConverter.Parse(pptxStream, options?.DefaultFont, options?.Culture), options, cancel);
    }

    /// <summary>Converts a PPTX file to a PDF written to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string pptxPath, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(pptxPath, options, cancel));

    /// <summary>Converts a PPTX stream to a PDF written to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream pptxStream, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var pdf = ConvertToPdf(pptxStream, options, cancel);
        output.Write(pdf, 0, pdf.Length);
    }
}
