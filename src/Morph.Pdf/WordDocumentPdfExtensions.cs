namespace Morph;

/// <summary>Adds <c>ExportToPdf</c> methods to <see cref="WordDocument"/>.</summary>
public static class WordDocumentPdfExtensions
{
    /// <summary>Renders the document to a PDF byte array.</summary>
    public static byte[] ExportToPdf(this WordDocument document, PdfExportOptions? options = null) =>
        document.ExportToPdf(options, default);

    /// <summary>Renders the document to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ExportToPdf(this WordDocument document, PdfExportOptions? options, Cancel cancel) =>
        PdfRenderer.Render(document.Document, options, cancel);

    /// <summary>Renders the document and writes the PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ExportToPdf(this WordDocument document, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, document.ExportToPdf(options, cancel));

    /// <summary>Renders the document and writes the PDF to <paramref name="output"/>.</summary>
    public static void ExportToPdf(this WordDocument document, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var bytes = document.ExportToPdf(options, cancel);
        output.Write(bytes, 0, bytes.Length);
    }
}
