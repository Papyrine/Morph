namespace Morph;

/// <summary>
/// Converts PowerPoint presentations to vector-text PDF using PdfSharp, one PDF page per slide. For
/// parse-once / export-many workflows, prefer <see cref="PowerPointDocument"/> plus its
/// <c>ExportToPdf</c> extension.
/// </summary>
public sealed class PdfPowerPointConverter
{
    /// <summary>Converts a PPTX file to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(string pptxPath, PdfExportOptions? options = null)
    {
        using var stream = File.OpenRead(pptxPath);
        return ConvertToPdf(stream, options);
    }

    /// <summary>Converts a PPTX stream to a PDF byte array.</summary>
    public static byte[] ConvertToPdf(Stream pptxStream, PdfExportOptions? options = null) =>
        PdfRenderer.Render(PowerPointConverter.Parse(pptxStream, options?.DefaultFont), options);

    /// <summary>Converts a PPTX file to a PDF written to <paramref name="outputPdfPath"/>.</summary>
    public static void ConvertToPdf(string pptxPath, string outputPdfPath, PdfExportOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, ConvertToPdf(pptxPath, options));

    /// <summary>Converts a PPTX stream to a PDF written to <paramref name="output"/>.</summary>
    public static void ConvertToPdf(Stream pptxStream, Stream output, PdfExportOptions? options = null)
    {
        var pdf = ConvertToPdf(pptxStream, options);
        output.Write(pdf, 0, pdf.Length);
    }
}
