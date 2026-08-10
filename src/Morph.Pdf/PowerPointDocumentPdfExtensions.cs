namespace Morph;

/// <summary>Adds <c>ExportToPdf</c> methods to <see cref="PowerPointDocument"/>.</summary>
public static class PowerPointDocumentPdfExtensions
{
    /// <summary>Renders the deck to a PDF byte array, one page per slide.</summary>
    public static byte[] ExportToPdf(this PowerPointDocument document, PdfExportOptions? options = null) =>
        PdfRenderer.Render(document.Document, options);

    /// <summary>Renders the deck and writes the PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ExportToPdf(this PowerPointDocument document, string outputPdfPath, PdfExportOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, document.ExportToPdf(options));

    /// <summary>Renders the deck and writes the PDF to <paramref name="output"/>.</summary>
    public static void ExportToPdf(this PowerPointDocument document, Stream output, PdfExportOptions? options = null)
    {
        var bytes = document.ExportToPdf(options);
        output.Write(bytes, 0, bytes.Length);
    }
}
