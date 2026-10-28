namespace Morph;

/// <summary>Adds <c>ExportToPdf</c> methods to <see cref="ExcelDocument"/>.</summary>
public static class ExcelDocumentPdfExtensions
{
    /// <summary>Renders the workbook to a PDF byte array.</summary>
    public static byte[] ExportToPdf(this ExcelDocument document, PdfExportOptions? options = null) =>
        document.ExportToPdf(options, Cancel.None);

    /// <summary>Renders the workbook to a PDF byte array, stopping when <paramref name="cancel"/> is cancelled.</summary>
    public static byte[] ExportToPdf(this ExcelDocument document, PdfExportOptions? options, Cancel cancel) =>
        PdfRenderer.Render(document.Document, options, cancel);

    /// <summary>Renders the workbook and writes the PDF to <paramref name="outputPdfPath"/>.</summary>
    public static void ExportToPdf(this ExcelDocument document, string outputPdfPath, PdfExportOptions? options = null, Cancel cancel = default) =>
        File.WriteAllBytes(outputPdfPath, document.ExportToPdf(options, cancel));

    /// <summary>Renders the workbook and writes the PDF to <paramref name="output"/>.</summary>
    public static void ExportToPdf(this ExcelDocument document, Stream output, PdfExportOptions? options = null, Cancel cancel = default)
    {
        var bytes = document.ExportToPdf(options, cancel);
        output.Write(bytes, 0, bytes.Length);
    }
}
