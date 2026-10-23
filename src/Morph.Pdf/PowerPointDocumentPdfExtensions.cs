namespace Morph;

/// <summary>Adds <c>ExportToPdf</c> methods to <see cref="PowerPointDocument"/>.</summary>
public static class PowerPointDocumentPdfExtensions
{
    /// <summary>Renders the deck to a PDF byte array, one page per slide.</summary>
    public static byte[] ExportToPdf(this PowerPointDocument document, PdfExportOptions? options = null) =>
        document.ExportToPdf(options, default);

    /// <summary>Renders the deck to a PDF byte array, one page per slide, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ExportToPdf(this PowerPointDocument document, PdfExportOptions? options, Cancel cancel) =>
        PdfRenderer.Render(document.Document, options, cancel);

    /// <summary>Renders the deck and writes the PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ExportToPdf(this PowerPointDocument document, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, document.ExportToPdf(options, cancel));

    /// <summary>Renders the deck and writes the PDF to <paramref name="output"/>.</summary>
    public static void ExportToPdf(this PowerPointDocument document, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var bytes = document.ExportToPdf(options, cancel);
        output.Write(bytes, 0, bytes.Length);
    }
}
