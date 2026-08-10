namespace Morph;

/// <summary>Adds <c>ExportToPdf</c> methods to <see cref="ExcelDocument"/>.</summary>
public static class ExcelDocumentPdfExtensions
{
    /// <summary>Renders the workbook to a PDF byte array.</summary>
    public static byte[] ExportToPdf(this ExcelDocument document, PdfExportOptions? options = null) =>
        PdfRenderer.Render(document.Document, options);

    /// <summary>Renders the workbook and writes the PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ExportToPdf(this ExcelDocument document, string outputPdfPath, PdfExportOptions? options = null) =>
        File.WriteAllBytes(outputPdfPath, document.ExportToPdf(options));

    /// <summary>Renders the workbook and writes the PDF to <paramref name="output"/>.</summary>
    public static void ExportToPdf(this ExcelDocument document, Stream output, PdfExportOptions? options = null)
    {
        var bytes = document.ExportToPdf(options);
        output.Write(bytes, 0, bytes.Length);
    }
}
