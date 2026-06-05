namespace Morph;

/// <summary>Adds <c>ExportToPdf</c> methods to <see cref="WordDocument"/>.</summary>
public static class WordDocumentPdfExtensions
{
    /// <summary>Renders the document to a PDF byte array.</summary>
    public static byte[] ExportToPdf(this WordDocument document, PdfExportOptions? options = null) =>
        PdfRenderer.Render(document.Document, options);

    /// <summary>Renders the document and writes the PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ExportToPdf(this WordDocument document, string outputPdfPath, PdfExportOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, document.ExportToPdf(options));

    /// <summary>Renders the document and writes the PDF to <paramref name="output"/>.</summary>
    public static void ExportToPdf(this WordDocument document, Stream output, PdfExportOptions? options = null)
    {
        var bytes = document.ExportToPdf(options);
        output.Write(bytes, 0, bytes.Length);
    }
}
